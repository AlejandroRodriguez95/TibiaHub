using Microsoft.Extensions.Options;

namespace TibiaHub.DataRefresh.NpcTrades;

public sealed class NpcTradeRefreshJob(
    NpcTradeSourceClient source,
    JsonSnapshotStore<NpcTradeCatalog> store,
    IOptionsMonitor<NpcTradeSourceOptions> options,
    TimeProvider clock) : IRefreshJob
{
    public string Name => "npc-trades";

    public async Task<DateTimeOffset?> LastSuccessAsync(CancellationToken token)
    {
        var snapshot = await store.ReadAsync(token);
        return snapshot?.SourceKey == options.CurrentValue.SourceKey ? snapshot.UpdatedAt : null;
    }

    public async Task RefreshAsync(CancellationToken token)
    {
        var configuration = options.CurrentValue;
        var npcs = new List<NpcPurchases>();
        if (configuration.Names.Length > 0)
        {
            var tasks = WikiTradeParser.DeliveryItems(await source.PageAsync("Delivery_Task", token));
            foreach (var name in configuration.Names)
            {
                var canonical = await source.ValidateNpcAsync(name, token);
                var items = WikiTradeParser.Purchases(await source.PageAsync(canonical, token), tasks);
                npcs.Add(new(canonical, items));
                // Public API default is 30 calls/minute; keep NPC validations below that rate.
                await Task.Delay(TimeSpan.FromSeconds(2), clock, token);
            }
        }
        if (configuration.SourceKey != options.CurrentValue.SourceKey)
            throw new InvalidOperationException("Source configuration changed during refresh; retry with the new list.");
        await store.WriteAsync(new(clock.GetUtcNow(), configuration.SourceKey, new(npcs)), token);
    }
}
