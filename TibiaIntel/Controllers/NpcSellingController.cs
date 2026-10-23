using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using TibiaIntel.Identity;
using Microsoft.Extensions.Options;
using TibiaIntel.DataRefresh;
using TibiaIntel.DataRefresh.NpcTrades;
using TibiaIntel.DataRefresh.Market;
using TibiaIntel.DataRefresh.Worlds;

namespace TibiaIntel.Controllers;

[ApiController]
[Route("api/tools/npc-selling")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NpcSellingController(
    JsonSnapshotStore<NpcTradeCatalog> store,
    IOptionsMonitor<NpcTradeSourceOptions> source,
    WorldListService worldList, JsonSnapshotStore<MarketWorldList> marketWorlds,
    JsonSnapshotStore<MarketItemIndex> marketItems,
    MarketWorldStore marketPrices, WorldRequestTracker requests,
    IOptionsMonitor<MarketSourceOptions> marketOptions,
    TimeProvider clock, UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken token, [FromQuery] string? world = null)
    {
        var snapshot = await store.ReadAsync(token);
        if (snapshot is null || snapshot.SourceKey != source.CurrentValue.SourceKey)
        {
            Response.Headers.RetryAfter = "30";
            return StatusCode(503, new { message = "NPC prices are not available yet. The data collector will retry automatically." });
        }
        var key = marketOptions.CurrentValue.SourceKey;
        var worldSnapshot = await marketWorlds.ReadAsync(token);
        var worlds = await worldList.GetWorldsAsync(token);
        if (string.IsNullOrWhiteSpace(world) && User.Identity?.IsAuthenticated == true)
        {
            var user = await userManager.GetUserAsync(User);
            var preferred = user is null ? null : (await userManager.GetClaimsAsync(user))
                .FirstOrDefault(c => c.Type == AccountController.DefaultWorldClaim)?.Value;
            if (preferred is not null && worlds.Contains(preferred, StringComparer.OrdinalIgnoreCase)) world = preferred;
        }
        var selected = string.IsNullOrWhiteSpace(world) ? null : worlds.FirstOrDefault(w => w.Equals(world.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(world) && selected is null)
            return BadRequest(new { message = "Select a world from the cached world list." });
        if (selected is not null) requests.Record(selected);
        var metadata = await marketItems.ReadAsync(token);
        var market = selected is null ? null : await marketPrices.For(selected).ReadAsync(token);
        var available = market?.SourceKey == key && metadata?.SourceKey == key;
        var index = available ? MarketItemMatcher.BuildIndex(metadata!.Data.Items) : [];
        var prices = available ? market!.Data.Items.ToDictionary(i => i.Id) : [];
        var basis = marketOptions.CurrentValue.AverageBasis == "sell" ? "sell" : "buy";
        var now = clock.GetUtcNow();
        DateTimeOffset? oldest = available && prices.Count > 0 ? prices.Values.Min(i => i.SourceUpdatedAt) : null;
        return Ok(new
        {
            snapshot.UpdatedAt,
            IsStale = clock.GetUtcNow() - snapshot.UpdatedAt > TimeSpan.FromHours(25),
            MarketValuesArePlaceholders = false,
            Worlds = worlds,
            SelectedWorld = selected,
            MarketAvailable = available,
            MarketUpdatedAt = available ? market!.UpdatedAt : (DateTimeOffset?)null,
            MarketSourceOldestAt = oldest,
            MarketIsStale = available && (now - market!.UpdatedAt > TimeSpan.FromHours(25) || oldest is not null && now - oldest > TimeSpan.FromHours(25)),
            MarketRefreshPending = selected is not null && worldSnapshot?.SourceKey == key && worldSnapshot.Data.PendingWorlds.Contains(selected),
            MarketAverageBasis = basis,
            Npcs = snapshot.Data.Npcs.Select(npc => new
            {
                npc.Name,
                Items = npc.Items.Select(item =>
                {
                    var price = index.TryGetValue(item.Name.Trim(), out var id) ? prices.GetValueOrDefault(id) : null;
                    var average = basis == "sell" ? price?.MonthlySellAverage : price?.MonthlyBuyAverage;
                    return new
                    {
                        item.Name, item.NpcPrice, item.IconUrl, item.IsDeliveryTask,
                        AverageMarketValue = average,
                        IsMarketHigher = average is not null && average > item.NpcPrice,
                        MarketSourceUpdatedAt = price?.SourceUpdatedAt
                    };
                })
            })
        });
    }
}
