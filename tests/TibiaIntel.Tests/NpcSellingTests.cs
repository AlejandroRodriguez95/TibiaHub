using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TibiaIntel.DataRefresh;
using TibiaIntel.DataRefresh.NpcTrades;

namespace TibiaIntel.Tests;

public sealed class NpcSellingTests
{
    private const string Buys = """
        <div id="npc-trade-sells"><table><tr><td></td><td><a href="/wiki/Wrong">Wrong</a></td><td>999</td></tr></table></div>
        <div id="npc-trade-buys"><h4>Buys</h4><table><tr><th></th><th>Item</th><th>Price</th></tr>
        <tr><td><img data-src="https://static.wikia.nocookie.net/tibia/golden.gif?a=1&amp;b=2"></td>
        <td><a href="/wiki/Golden_Legs">Golden Legs</a><sup>1</sup></td><td>30,000 <span>gold</span></td></tr>
        <tr><td></td><td><a href="/wiki/Trader%27s_Token">Trader&#39;s Token</a></td><td>150</td></tr></table></div>
        """;

    [Fact]
    public void ParsesOnlyPurchasesWithExactDeliveryMatchesAndDecodedNames()
    {
        var items = WikiTradeParser.Purchases(Buys, new HashSet<string>(["golden legs", "Token"], StringComparer.OrdinalIgnoreCase));
        Assert.Equal(2, items.Count);
        Assert.Equal("Golden Legs", items[0].Name);
        Assert.Equal(30000, items[0].NpcPrice);
        Assert.True(items[0].IsDeliveryTask);
        Assert.EndsWith("?a=1&b=2", items[0].IconUrl);
        Assert.Equal("Trader's Token", items[1].Name);
        Assert.Null(items[1].IconUrl);
        Assert.False(items[1].IsDeliveryTask);
    }

    [Fact]
    public void ParsesDeliveryTablesWithoutUnrelatedLinks()
    {
        var names = WikiTradeParser.DeliveryItems("""
            <a href="/wiki/Wrong">Wrong</a><table><tr><th>Name</th><th>Min. Required</th></tr>
            <tr><td>icon</td><td><a href="/wiki/Golden_Legs">Golden Legs</a></td><td>5</td><td>10</td><td>30,000</td></tr></table>
            """);
        Assert.Equal(["Golden Legs"], names);
        Assert.Throws<InvalidDataException>(() => WikiTradeParser.DeliveryItems("<p>Service unavailable</p>"));
        Assert.Throws<InvalidDataException>(() => WikiTradeParser.Purchases(Buys.Replace("30,000", "unknown"), names));
        Assert.Throws<InvalidDataException>(() => WikiTradeParser.Purchases("<p>Not found</p>", names));
    }

    [Fact]
    public void DailyScheduleUsesViennaLocalTimeAndCatchesUp()
    {
        var schedule = new RefreshSchedule { DailyAt = "10:30" };
        Assert.Equal(DateTimeOffset.Parse("2026-01-10T09:30:00Z"), schedule.DueAt(DateTimeOffset.Parse("2026-01-10T11:00:00Z"), null));
        Assert.Equal(DateTimeOffset.Parse("2026-07-10T08:30:00Z"), schedule.DueAt(DateTimeOffset.Parse("2026-07-10T11:00:00Z"), null));
        Assert.Equal(DateTimeOffset.Parse("2026-07-09T08:30:00Z"), schedule.DueAt(DateTimeOffset.Parse("2026-07-10T08:29:00Z"), null));
        var interval = new RefreshSchedule { IntervalMinutes = 60 };
        var last = DateTimeOffset.Parse("2026-07-10T08:00:00Z");
        Assert.Equal(last.AddHours(1), interval.DueAt(last.AddMinutes(20), last));
        Assert.Throws<InvalidOperationException>(() => new RefreshSchedule { DailyAt = "10:30", IntervalMinutes = 60 }.DueAt(last, last));
    }

    [Fact]
    public void NormalizesNpcConfigurationWithoutSubstringMatching()
    {
        var options = new NpcTradeSourceOptions { NpcNames = [" Rashid ", "rashid", "", "Yasir"] };
        Assert.Equal(2, options.Names.Length);
        Assert.Equal("Rashid", options.Names[0]);
    }

    [Fact]
    public async Task EndpointServesPersistentSnapshotAndExplicitUnavailableState()
    {
        var cache = Path.Combine(Path.GetTempPath(), "tibiaintel-refresh-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var app = CreateApp(cache))
            {
                using var client = app.CreateClient();
                var missing = await client.GetAsync("/tools/npc-selling");
                Assert.Equal(HttpStatusCode.ServiceUnavailable, missing.StatusCode);
                var store = app.Services.GetRequiredService<JsonSnapshotStore<NpcTradeCatalog>>();
                var options = app.Services.GetRequiredService<IOptionsMonitor<NpcTradeSourceOptions>>().CurrentValue;
                var catalog = new NpcTradeCatalog([new("Rashid", [new("Golden Legs", 30000, null, true)])]);
                await store.WriteAsync(new(DateTimeOffset.UtcNow.AddHours(-26), options.SourceKey, catalog), default);
                var response = await client.GetFromJsonAsync<JsonElement>("/tools/npc-selling");
                Assert.True(response.GetProperty("isStale").GetBoolean());
                Assert.False(response.GetProperty("marketValuesArePlaceholders").GetBoolean());
                var item = response.GetProperty("npcs")[0].GetProperty("items")[0];
                Assert.Equal(30000, item.GetProperty("npcPrice").GetInt64());
                Assert.Equal(JsonValueKind.Null, item.GetProperty("averageMarketValue").ValueKind);
                Assert.True(item.GetProperty("isDeliveryTask").GetBoolean());
            }
            // A second host must load the saved file rather than lose data on restart.
            using var restarted = CreateApp(cache);
            using var restartedClient = restarted.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await restartedClient.GetAsync("/tools/npc-selling")).StatusCode);
            using var changed = CreateApp(cache, "Yasir");
            using var changedClient = changed.CreateClient();
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await changedClient.GetAsync("/tools/npc-selling")).StatusCode);
        }
        finally { if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true); }
    }

    [Fact]
    public async Task FailedRefreshKeepsPreviousCompleteSnapshot()
    {
        var cache = Path.Combine(Path.GetTempPath(), "tibiaintel-refresh-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var app = CreateApp(cache);
            using var client = app.CreateClient();
            var store = app.Services.GetRequiredService<JsonSnapshotStore<NpcTradeCatalog>>();
            var options = app.Services.GetRequiredService<IOptionsMonitor<NpcTradeSourceOptions>>();
            var original = new DataSnapshot<NpcTradeCatalog>(DateTimeOffset.UtcNow.AddDays(-1), options.CurrentValue.SourceKey,
                new([new("Rashid", [new("Golden Legs", 30000, null, true)])]));
            await store.WriteAsync(original, default);
            using var http = new HttpClient(new FailingSource());
            var job = new NpcTradeRefreshJob(new NpcTradeSourceClient(http, options, app.Services.GetRequiredService<UpstreamRequestPolicy>()), store, options, TimeProvider.System);
            await Assert.ThrowsAsync<HttpRequestException>(() => job.RefreshAsync(default));
            Assert.Equal(original, await store.ReadAsync(default));
            Assert.Equal(original.UpdatedAt, await job.LastSuccessAsync(default));
        }
        finally { if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true); }
    }

    private sealed class FailingSource : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static WebApplicationFactory<Program> CreateApp(string cache, string npc = "Rashid") =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataRefresh:Enabled"] = "false",
                ["DataRefresh:CacheDirectory"] = cache,
                ["NpcTradeSource:NpcNames:0"] = npc
            })));
}
