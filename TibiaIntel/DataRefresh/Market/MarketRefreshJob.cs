using Microsoft.Extensions.Options;
using TibiaIntel.DataRefresh.Worlds;

namespace TibiaIntel.DataRefresh.Market;

public sealed class MarketRefreshJob(
    MarketSourceClient source, WorldListService worldList, JsonSnapshotStore<MarketWorldList> worlds,
    JsonSnapshotStore<MarketItemIndex> items, MarketWorldStore prices, WorldRequestTracker requests,
    IOptionsMonitor<MarketSourceOptions> options, TimeProvider clock,
    ILogger<MarketRefreshJob> logger) : IRefreshJob
{
    public string Name => "market-prices";

    public async Task<DateTimeOffset?> LastSuccessAsync(CancellationToken token)
    {
        var snapshot = await worlds.ReadAsync(token);
        var key = options.CurrentValue.SourceKey;
        if (snapshot?.SourceKey != key || snapshot.Data.PendingWorlds.Length != 0
            || (await items.ReadAsync(token))?.SourceKey != key) return null;
        foreach (var world in snapshot.Data.Worlds)
        {
            var cached = await prices.For(world).ReadAsync(token);
            if (cached?.SourceKey != key || cached.Data.ProviderUpdatedAt is null) return null;
        }
        return snapshot.UpdatedAt;
    }

    public async Task RefreshAsync(CancellationToken token)
    {
        var key = options.CurrentValue.SourceKey;
        var directory = await worldList.GetWorldsAsync(token);
        if (directory.Length == 0) throw new InvalidOperationException("The world list has not been collected yet.");
        // Recheck statuses on retries too: successful unchanged worlds stay cached,
        // while a newer provider scan can join the next attempt.
        var statuses = (await source.WorldStatusesAsync(token))
            .Where(w => directory.Contains(w.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (statuses.Length == 0) throw new InvalidDataException("No directory worlds are supported by the market source.");
        var metadata = await source.ItemsAsync(token);
        EnsureSource(key);
        await items.WriteAsync(new(clock.GetUtcNow(), key, metadata), token);

        var updates = statuses.ToDictionary(w => w.Name, w => w.LastUpdate, StringComparer.OrdinalIgnoreCase);
        var pending = new List<string>();
        foreach (var status in statuses)
        {
            var cached = await prices.For(status.Name).ReadAsync(token);
            if (NeedsRefresh(cached, key, status.LastUpdate)) pending.Add(status.Name);
        }
        var cycle = new MarketWorldList(statuses.Select(w => w.Name).ToArray(), clock.GetUtcNow(), pending.ToArray(), updates);
        await worlds.WriteAsync(new(clock.GetUtcNow(), key, cycle), token);

        var unattempted = pending.ToList();
        while (unattempted.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            EnsureSource(key);
            // Reorder between fetches so new user demand can influence this cycle.
            var world = unattempted.OrderByDescending(requests.Count)
                .ThenByDescending(w => updates[w]).ThenBy(w => w, StringComparer.OrdinalIgnoreCase).First();
            unattempted.Remove(world);
            try
            {
                var store = prices.For(world);
                var cached = await store.ReadAsync(token);
                if (NeedsRefresh(cached, key, updates[world]))
                {
                    var data = await source.PricesAsync(world, token);
                    EnsureSource(key);
                    // Publish the scan marker with prices, so a crash before progress
                    // publication does not cause a successful world to be fetched again.
                    await store.WriteAsync(new(clock.GetUtcNow(), key, data with { ProviderUpdatedAt = updates[world] }), token);
                }
                var remaining = pending.Where(name => name != world).ToList();
                await worlds.WriteAsync(new(clock.GetUtcNow(), key, cycle with { PendingWorlds = remaining.ToArray() }), token);
                pending = remaining;
                logger.LogInformation("Market prices collected for {World}; {Remaining} worlds remaining", world, pending.Count);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception) { logger.LogWarning(exception, "Market collection failed for {World}; retaining previous prices", world); }
        }
        if (pending.Count > 0) throw new InvalidOperationException($"Market refresh incomplete: {pending.Count} worlds will retry.");
    }

    private static bool NeedsRefresh(DataSnapshot<MarketWorldPrices>? cached, string key, DateTimeOffset updated) =>
        cached?.SourceKey != key || cached.Data.ProviderUpdatedAt is null || cached.Data.ProviderUpdatedAt < updated;

    private void EnsureSource(string key)
    {
        if (options.CurrentValue.SourceKey != key) throw new InvalidOperationException("Market source changed during collection.");
    }
}
