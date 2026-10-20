using Microsoft.Extensions.Options;

namespace TibiaHub.DataRefresh.Market;

public sealed class MarketRefreshJob(
    MarketSourceClient source, JsonSnapshotStore<MarketWorldList> worlds,
    JsonSnapshotStore<MarketItemIndex> items, MarketWorldStore prices,
    IOptionsMonitor<MarketSourceOptions> options, IOptionsMonitor<RefreshOptions> refreshOptions, TimeProvider clock,
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
            if ((await prices.For(world).ReadAsync(token))?.SourceKey != key) return null;
        return snapshot.UpdatedAt;
    }

    public async Task RefreshAsync(CancellationToken token)
    {
        var key = options.CurrentValue.SourceKey;
        var previous = await worlds.ReadAsync(token);
        var schedule = refreshOptions.CurrentValue.Schedules.SingleOrDefault(s => s.Job == Name);
        var due = schedule?.DailyAt is not null ? schedule.DueAt(clock.GetUtcNow(), previous?.UpdatedAt) : DateTimeOffset.MinValue;
        MarketWorldList cycle;
        if (previous?.SourceKey == key && previous.Data.PendingWorlds.Length > 0 && previous.Data.CycleStartedAt >= due)
            cycle = previous.Data;
        else
        {
            var names = await source.WorldsAsync(token);
            var metadata = await source.ItemsAsync(token);
            await items.WriteAsync(new(clock.GetUtcNow(), key, metadata), token);
            cycle = new(names, clock.GetUtcNow(), names);
            await worlds.WriteAsync(new(clock.GetUtcNow(), key, cycle), token);
        }

        var pending = cycle.PendingWorlds.ToList();
        foreach (var world in cycle.PendingWorlds)
        {
            token.ThrowIfCancellationRequested();
            if (options.CurrentValue.SourceKey != key) throw new InvalidOperationException("Market source changed during collection.");
            try
            {
                var store = prices.For(world);
                var cached = await store.ReadAsync(token);
                if (cached?.SourceKey != key || cached.UpdatedAt < cycle.CycleStartedAt)
                {
                    var data = await source.PricesAsync(world, token);
                    await store.WriteAsync(new(clock.GetUtcNow(), key, data), token);
                }
                var remaining = pending.Where(name => name != world).ToList();
                // Publish progress so successful worlds work while others collect/retry.
                await worlds.WriteAsync(new(clock.GetUtcNow(), key, cycle with { PendingWorlds = remaining.ToArray() }), token);
                pending = remaining;
                logger.LogInformation("Market prices collected for {World}; {Remaining} worlds remaining", world, pending.Count);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception) { logger.LogWarning(exception, "Market collection failed for {World}; retaining previous prices", world); }
        }
        if (pending.Count > 0) throw new InvalidOperationException($"Market refresh incomplete: {pending.Count} worlds will retry.");
    }
}
