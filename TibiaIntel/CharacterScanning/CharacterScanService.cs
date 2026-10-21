using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TibiaIntel.Data;

namespace TibiaIntel.CharacterScanning;

public sealed class CharacterScanService(ApplicationDbContext db, IOptions<CharacterScanOptions> options, TimeProvider clock)
{
    public async Task<CharacterScanResult> ScanAsync(string world, string name, CancellationToken token)
    {
        var cutoff = clock.GetUtcNow().AddDays(-14);
        var normalized = SessionRecorder.Normalize(name);
        var character = await db.ScannedCharacters.AsNoTracking().SingleOrDefaultAsync(c => c.World == world && c.NormalizedName == normalized, token);
        var observations = await db.WorldObservations.AsNoTracking().Where(o => o.World == world && o.SourceAt >= cutoff)
            .OrderBy(o => o.SourceAt).Select(o => new WorldObservation { SourceAt = o.SourceAt, Exclusion = o.Exclusion }).ToListAsync(token);
        var latest = observations.LastOrDefault()?.SourceAt;
        var historyStart = observations.FirstOrDefault()?.SourceAt;
        var logouts = character is null ? [] : await db.OnlineSessions.AsNoTracking()
            .Where(s => s.CharacterId == character.Id && s.LogoutAt >= cutoff && s.LogoutAt != null)
            .OrderBy(s => s.LogoutAt).ToListAsync(token);
        var usable = 0;
        var totals = new Dictionary<string, ScanCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var logout in logouts)
        {
            var end = logout.LogoutAt!.Value;
            if (!logout.LogoutReliable || !HasCompleteWindow(observations, end, options.Value.MaximumGapSeconds)) continue;
            usable++;
            var until = end.AddMinutes(2);
            var candidates = await db.OnlineSessions.AsNoTracking().Where(s => s.Character.World == world
                && s.CharacterId != character!.Id && s.LoginReliable && s.LoginAt >= end && s.LoginAt <= until)
                .Select(s => new { s.Character.Name, s.LoginAt }).ToListAsync(token);
            // Reconnecting repeatedly cannot earn multiple awards for one target logout.
            foreach (var candidate in candidates.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                var firstMinute = candidate.Min(c => c.LoginAt) <= end.AddMinutes(1);
                totals.TryGetValue(candidate.Key, out var previous);
                totals[candidate.Key] = new(candidate.Key, (previous?.Score ?? 0) + (firstMinute ? 10 : 5),
                    (previous?.FirstMinuteMatches ?? 0) + (firstMinute ? 1 : 0),
                    (previous?.SecondMinuteMatches ?? 0) + (firstMinute ? 0 : 1));
            }
        }
        return new(world, character?.Name ?? name.Trim(), cutoff, historyStart, latest, logouts.Count, usable,
            totals.Values.OrderByDescending(c => c.Score).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public static bool HasCompleteWindow(IReadOnlyList<WorldObservation> observations, DateTimeOffset logout, int maximumGapSeconds)
    {
        var end = logout.AddMinutes(2);
        var previous = logout;
        foreach (var observation in observations.Where(o => o.SourceAt > logout))
        {
            if (observation.Exclusion != "" || observation.SourceAt!.Value - previous > TimeSpan.FromSeconds(maximumGapSeconds)) return false;
            previous = observation.SourceAt.Value;
            if (previous >= end) return true;
        }
        return false;
    }
}
