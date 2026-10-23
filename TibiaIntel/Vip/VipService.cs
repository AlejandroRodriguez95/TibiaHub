using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TibiaIntel.CharacterScanning;
using TibiaIntel.Data;
using TibiaIntel.DataRefresh;

namespace TibiaIntel.Vip;

public sealed class VipService(ApplicationDbContext db, IVipCharacterResolver resolver,
    IOptionsMonitor<VipOptions> options, IOptionsMonitor<CharacterScanOptions> scan,
    IOptionsMonitor<RefreshOptions> refresh, TimeProvider clock)
{
    public async Task<VipSnapshot> ReadAsync(string userId, CancellationToken token)
    {
        // Keep memberships, observations, and sessions from the same committed database view.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        var captured = clock.GetUtcNow();
        var entries = await db.VipCharacters.AsNoTracking().Where(c => c.UserId == userId)
            .Include(c => c.Memberships).OrderBy(c => c.Name).ThenBy(c => c.Id).ToArrayAsync(token);
        var groups = await db.VipGroups.AsNoTracking().Where(g => g.UserId == userId).OrderBy(g => g.Name).ThenBy(g => g.Id)
            .Select(g => new VipGroupResponse(g.Id, g.Name)).ToArrayAsync(token);
        var worlds = entries.Select(c => c.World).Distinct().ToArray();
        var names = entries.Select(c => c.NormalizedName).ToArray();
        var observations = await db.WorldObservations.AsNoTracking().Where(o => worlds.Contains(o.World)
            && !db.WorldObservations.Any(n => n.World == o.World && (n.FetchedAt > o.FetchedAt
                || (n.FetchedAt == o.FetchedAt && n.Id > o.Id)))).ToDictionaryAsync(o => o.World, token);
        var online = (await db.OnlineSessions.AsNoTracking().Where(s => s.LogoutAt == null
            && worlds.Contains(s.Character.World) && names.Contains(s.Character.NormalizedName))
            .Select(s => new { s.Character.World, s.Character.NormalizedName }).ToArrayAsync(token))
            .Select(c => (c.World, c.NormalizedName)).ToHashSet();
        var config = options.CurrentValue;
        var enabled = scan.CurrentValue.Enabled && refresh.CurrentValue.Enabled;
        var result = entries.Select(c =>
        {
            observations.TryGetValue(c.World, out var observation);
            var usable = enabled && IsFresh(observation, captured, config.StatusMaxAgeSeconds);
            return new VipEntry(c.Id, c.Name, c.World, c.Memberships.Select(m => m.GroupId).Order().ToArray(),
                usable ? online.Contains((c.World, c.NormalizedName)) ? "online" : "offline" : "unknown", observation?.SourceAt);
        }).ToArray();
        await transaction.CommitAsync(token);
        return new(captured, config.MaxCharactersPerUser, config.MaxGroupsPerUser, result, groups);
    }

    public static bool IsFresh(WorldObservation? observation, DateTimeOffset now, int maxAge) =>
        observation?.SourceAt is { } source && source <= now.AddSeconds(5)
        && now - source <= TimeSpan.FromSeconds(maxAge)
        && now - observation.FetchedAt <= TimeSpan.FromSeconds(maxAge)
        && observation.Exclusion is not ("fetch-failed" or "world-offline");

    public async Task AddAsync(string userId, string name, CancellationToken token)
    {
        name = name.Trim();
        if (name.Length is 0 or > 100) throw new VipException(400, "Enter a character name of up to 100 characters.");
        if (await db.VipCharacters.CountAsync(c => c.UserId == userId, token) >= options.CurrentValue.MaxCharactersPerUser)
            throw new VipException(400, "Your VIP character limit has been reached.");
        var identity = await resolver.ResolveAsync(name, token);
        await MutateAsync(userId, async () =>
        {
            var normalized = SessionRecorder.Normalize(identity.Name);
            if (await db.VipCharacters.AnyAsync(c => c.UserId == userId && c.NormalizedName == normalized, token))
                throw new VipException(409, "This character is already on your VIP list.");
            if (await db.VipCharacters.CountAsync(c => c.UserId == userId, token) >= options.CurrentValue.MaxCharactersPerUser)
                throw new VipException(400, "Your VIP character limit has been reached.");
            db.VipCharacters.Add(new() { UserId = userId, Name = identity.Name, NormalizedName = normalized, World = identity.World });
        }, token);
    }

    public Task RemoveAsync(string userId, long id, CancellationToken token) => MutateAsync(userId, async () =>
    {
        var entry = await OwnedCharacter(userId, id, token);
        db.VipCharacters.Remove(entry);
    }, token);

    public Task SaveGroupAsync(string userId, long? id, string name, CancellationToken token) => MutateAsync(userId, async () =>
    {
        name = name.Trim();
        if (name.Length is 0 or > 100) throw new VipException(400, "Enter a group name of up to 100 characters.");
        if (id is { } groupId)
        {
            var group = await db.VipGroups.SingleOrDefaultAsync(g => g.UserId == userId && g.Id == groupId, token)
                ?? throw new VipException(404, "Group not found.");
            group.Name = name;
        }
        else
        {
            if (await db.VipGroups.CountAsync(g => g.UserId == userId, token) >= options.CurrentValue.MaxGroupsPerUser)
                throw new VipException(400, "Your VIP group limit has been reached.");
            db.VipGroups.Add(new() { UserId = userId, Name = name });
        }
    }, token);

    public Task DeleteGroupAsync(string userId, long id, CancellationToken token) => MutateAsync(userId, async () =>
    {
        var group = await db.VipGroups.SingleOrDefaultAsync(g => g.UserId == userId && g.Id == id, token)
            ?? throw new VipException(404, "Group not found.");
        db.VipGroups.Remove(group); // Cascades memberships only, keeping characters.
    }, token);

    public Task SetGroupsAsync(string userId, long id, long[] groupIds, CancellationToken token) => MutateAsync(userId, async () =>
    {
        var entry = await OwnedCharacter(userId, id, token);
        var ids = groupIds.Distinct().ToArray();
        if (await db.VipGroups.CountAsync(g => g.UserId == userId && ids.Contains(g.Id), token) != ids.Length)
            throw new VipException(404, "One or more groups were not found.");
        var old = await db.VipMemberships.Where(m => m.CharacterId == entry.Id).ToArrayAsync(token);
        db.VipMemberships.RemoveRange(old.Where(m => !ids.Contains(m.GroupId)));
        foreach (var group in ids.Except(old.Select(m => m.GroupId)))
            db.VipMemberships.Add(new() { CharacterId = id, GroupId = group });
    }, token);

    private async Task<VipCharacter> OwnedCharacter(string userId, long id, CancellationToken token) =>
        await db.VipCharacters.SingleOrDefaultAsync(c => c.UserId == userId && c.Id == id, token)
        ?? throw new VipException(404, "Character not found on your VIP list.");

    private async Task MutateAsync(string userId, Func<Task> action, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        // Serialize writes per account across concurrent requests; limits cannot be bypassed by racing inserts.
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {userId} FOR UPDATE")
            .SingleOrDefaultAsync(token);
        if (user is null) throw new VipException(401, "Log in to use your VIP list.");
        await action();
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }
}
