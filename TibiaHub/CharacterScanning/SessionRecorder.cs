using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TibiaHub.Data;

namespace TibiaHub.CharacterScanning;

public sealed class SessionRecorder(ApplicationDbContext db, IOptions<CharacterScanOptions> options)
{
    public async Task RecordAsync(OnlineWorldSnapshot snapshot, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var previous = await db.WorldObservations.Where(o => o.World == snapshot.World && o.SourceAt != null)
            .OrderByDescending(o => o.SourceAt).FirstOrDefaultAsync(token);
        if (previous?.SourceKey == snapshot.SourceKey && snapshot.SourceAt <= previous.SourceAt) return;
        var open = await db.OnlineSessions.Include(s => s.Character)
            .Where(s => s.Character.World == snapshot.World && s.LogoutAt == null).ToListAsync(token);
        var names = snapshot.Available ? snapshot.Names.ToDictionary(Normalize, n => n) : new Dictionary<string, string>();
        var oldNames = open.Select(s => s.Character.NormalizedName).ToHashSet();
        var departed = open.Where(s => !names.ContainsKey(s.Character.NormalizedName)).ToArray();
        var arrivals = names.Where(n => !oldNames.Contains(n.Key)).ToArray();
        var config = options.Value;
        var reason = "";
        if (previous is null) reason = "initial-baseline";
        else if (previous.SourceKey != snapshot.SourceKey) reason = "source-changed";
        else if (snapshot.SourceAt - previous.SourceAt > TimeSpan.FromSeconds(config.MaximumGapSeconds)
            || await db.WorldObservations.AnyAsync(o => o.World == snapshot.World && o.SourceAt == null && o.FetchedAt > previous.FetchedAt, token))
            reason = "collection-gap";
        if (!snapshot.Available) reason = "world-offline";
        else if (OverlapsServerSave(previous?.SourceAt ?? snapshot.SourceAt, snapshot.SourceAt, config)) reason = "server-save";
        else if (previous is not null &&
            ((departed.Length >= config.MassChangeMinimum && departed.Length >= open.Count * config.MassChangeFraction)
            || (arrivals.Length >= config.MassChangeMinimum && arrivals.Length >= Math.Max(1, open.Count) * config.MassChangeFraction)))
            reason = "mass-change";
        if (reason == "" && await db.WorldObservations.AnyAsync(o => o.World == snapshot.World
                && o.SourceAt >= snapshot.SourceAt.AddMinutes(-2)
                && (o.Exclusion == "mass-change" || o.Exclusion == "world-offline" || o.Exclusion == "server-save"), token))
            reason = "recovery";
        var reliable = reason == "";
        foreach (var session in departed)
        {
            session.LogoutAt = snapshot.SourceAt;
            session.LogoutAfter = previous?.SourceAt;
            session.LogoutReliable = reliable;
        }
        var arrivingNames = arrivals.Select(a => a.Key).ToArray();
        var characters = await db.ScannedCharacters.Where(c => c.World == snapshot.World && arrivingNames.Contains(c.NormalizedName))
            .ToDictionaryAsync(c => c.NormalizedName, token);
        foreach (var arrival in arrivals)
        {
            if (!characters.TryGetValue(arrival.Key, out var character))
            {
                character = new() { World = snapshot.World, Name = arrival.Value, NormalizedName = arrival.Key };
                db.ScannedCharacters.Add(character);
            }
            else character.Name = arrival.Value;
            db.OnlineSessions.Add(new() { Character = character, LoginAt = snapshot.SourceAt,
                LoginAfter = previous?.SourceAt, LoginReliable = reliable });
        }
        db.WorldObservations.Add(new() { World = snapshot.World, SourceKey = snapshot.SourceKey,
            SourceAt = snapshot.SourceAt, FetchedAt = snapshot.FetchedAt, OnlineCount = names.Count, Exclusion = reason });
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    public async Task RecordFailureAsync(string world, DateTimeOffset fetchedAt, CancellationToken token)
    {
        db.WorldObservations.Add(new() { World = world, FetchedAt = fetchedAt, Exclusion = "fetch-failed" });
        await db.SaveChangesAsync(token);
    }

    public async Task PruneAsync(DateTimeOffset now, CancellationToken token)
    {
        var cutoff = now.AddDays(-14);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.OnlineSessions.Where(s => s.LogoutAt < cutoff).ExecuteDeleteAsync(token);
        // Retain the most recent valid observation per world, even for inactive worlds.
        await db.WorldObservations.Where(o => o.FetchedAt < cutoff && (o.SourceAt == null
            || db.WorldObservations.Any(n => n.World == o.World && n.SourceAt > o.SourceAt))).ExecuteDeleteAsync(token);
        await db.ScannedCharacters.Where(c => !db.OnlineSessions.Any(s => s.CharacterId == c.Id)).ExecuteDeleteAsync(token);
        await transaction.CommitAsync(token);
    }

    public static string Normalize(string name) => name.Trim().ToUpperInvariant();

    public static bool OverlapsServerSave(DateTimeOffset start, DateTimeOffset end, CharacterScanOptions options)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(options.ServerSaveTimeZone);
        var from = TimeZoneInfo.ConvertTime(start, zone).DateTime;
        var to = TimeZoneInfo.ConvertTime(end, zone).DateTime;
        var saveStart = TimeOnly.ParseExact(options.ServerSaveStart, "HH:mm");
        var saveEnd = TimeOnly.ParseExact(options.ServerSaveEnd, "HH:mm");
        for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
            if (from <= day + saveEnd.ToTimeSpan() && to >= day + saveStart.ToTimeSpan()) return true;
        return false;
    }
}
