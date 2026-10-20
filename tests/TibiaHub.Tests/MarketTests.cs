using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using TibiaHub.DataRefresh;
using TibiaHub.DataRefresh.Market;
using TibiaHub.DataRefresh.NpcTrades;

namespace TibiaHub.Tests;

public sealed class MarketTests
{
    [Fact]
    public void MatchingPrefersWikiTitlesAndRejectsAmbiguousNames()
    {
        var index = MarketItemMatcher.BuildIndex([
            new(3364, "Golden Legs", "golden legs"), new(1, "Special Flask (A)", "special flask"), new(2, "Special Flask (B)", "special flask")]);
        Assert.Equal(3364, index["golden legs"]);
        Assert.False(index.ContainsKey("special flask"));
        Assert.Equal(1, index["Special Flask (A)"]);
        Assert.DoesNotContain("Golden", index.Keys);
    }

    [Fact]
    public async Task WorldSelectionJoinsRealAveragesAndKeepsBothIndicators()
    {
        var cache = Path.Combine(Path.GetTempPath(), "tibiahub-market-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var app = CreateApp(cache);
            using var client = app.CreateClient();
            var services = app.Services;
            var key = services.GetRequiredService<IOptionsMonitor<MarketSourceOptions>>().CurrentValue.SourceKey;
            var npcKey = services.GetRequiredService<IOptionsMonitor<NpcTradeSourceOptions>>().CurrentValue.SourceKey;
            var now = DateTimeOffset.UtcNow;
            await services.GetRequiredService<JsonSnapshotStore<NpcTradeCatalog>>().WriteAsync(new(now, npcKey,
                new([new("Rashid", [new("Golden Legs", 30000, null, true), new("Unknown", 10, null, false)])])), default);
            await services.GetRequiredService<JsonSnapshotStore<MarketWorldList>>().WriteAsync(new(now, key,
                new(["Antica", "Secura"], now, [])), default);
            await services.GetRequiredService<JsonSnapshotStore<MarketItemIndex>>().WriteAsync(new(now, key,
                new([new(3364, "Golden Legs", "golden legs")])), default);
            var prices = services.GetRequiredService<MarketWorldStore>();
            await prices.For("Antica").WriteAsync(new(now, key, new("Antica", [new(3364, 35000, 40000, now.AddDays(-5))])), default);
            await prices.For("Secura").WriteAsync(new(now, key, new("Secura", [new(3364, 30000, 40000, now)])), default);

            var antica = await client.GetFromJsonAsync<JsonElement>("/tools/npc-selling?world=antica");
            var item = antica.GetProperty("npcs")[0].GetProperty("items")[0];
            Assert.Equal("Antica", antica.GetProperty("selectedWorld").GetString());
            Assert.True(antica.GetProperty("marketIsStale").GetBoolean());
            Assert.Equal(35000, item.GetProperty("averageMarketValue").GetInt64());
            Assert.True(item.GetProperty("isMarketHigher").GetBoolean());
            Assert.True(item.GetProperty("isDeliveryTask").GetBoolean());
            Assert.Equal(JsonValueKind.Null, antica.GetProperty("npcs")[0].GetProperty("items")[1].GetProperty("averageMarketValue").ValueKind);
            var secura = await client.GetFromJsonAsync<JsonElement>("/tools/npc-selling?world=Secura");
            Assert.False(secura.GetProperty("npcs")[0].GetProperty("items")[0].GetProperty("isMarketHigher").GetBoolean());
            Assert.False(secura.GetProperty("marketIsStale").GetBoolean());
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/tools/npc-selling?world=NotAWorld")).StatusCode);
            var unselected = await client.GetFromJsonAsync<JsonElement>("/tools/npc-selling");
            Assert.False(unselected.GetProperty("marketAvailable").GetBoolean());

            using var sellApp = CreateApp(cache, "sell");
            using var sellClient = sellApp.CreateClient();
            var sell = await sellClient.GetFromJsonAsync<JsonElement>("/tools/npc-selling?world=Secura");
            Assert.Equal(40000, sell.GetProperty("npcs")[0].GetProperty("items")[0].GetProperty("averageMarketValue").GetInt64());
        }
        finally { if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true); }
    }

    [Fact]
    public async Task CollectorCachesAllItemsAndResumesOnlyFailedWorlds()
    {
        var cache = Path.Combine(Path.GetTempPath(), "tibiahub-market-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var app = CreateApp(cache);
            using var client = app.CreateClient();
            var services = app.Services;
            var options = services.GetRequiredService<IOptionsMonitor<MarketSourceOptions>>();
            using var handler = new FakeSource();
            using var http = new HttpClient(handler);
            var worlds = services.GetRequiredService<JsonSnapshotStore<MarketWorldList>>();
            var stores = services.GetRequiredService<MarketWorldStore>();
            var job = new MarketRefreshJob(new(http, options, TimeProvider.System), worlds,
                services.GetRequiredService<JsonSnapshotStore<MarketItemIndex>>(), stores, options,
                services.GetRequiredService<IOptionsMonitor<RefreshOptions>>(), TimeProvider.System, NullLogger<MarketRefreshJob>.Instance);
            await Assert.ThrowsAsync<InvalidOperationException>(() => job.RefreshAsync(default));
            Assert.Null(await job.LastSuccessAsync(default));
            Assert.Equal(["Secura"], (await worlds.ReadAsync(default))!.Data.PendingWorlds);
            var antica = await stores.For("Antica").ReadAsync(default);
            Assert.Equal(2, antica!.Data.Items.Count); // All market rows, not an NPC-specific subset.
            Assert.Null(antica.Data.Items[1].MonthlyBuyAverage); // -1 sentinel is not a price.
            handler.FailSecura = false;
            await job.RefreshAsync(default);
            Assert.NotNull(await job.LastSuccessAsync(default));
            Assert.Empty((await worlds.ReadAsync(default))!.Data.PendingWorlds);
            Assert.Equal(1, handler.AnticaRequests); // Already collected worlds are not downloaded again on retry.
            var complete = (await worlds.ReadAsync(default))!;
            // An unfinished older cycle must not prevent the next daily refresh of successful worlds.
            await worlds.WriteAsync(complete with { Data = complete.Data with
                { CycleStartedAt = DateTimeOffset.UtcNow.AddDays(-2), PendingWorlds = ["Secura"] } }, default);
            await job.RefreshAsync(default);
            Assert.Equal(2, handler.AnticaRequests);
        }
        finally { if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true); }
    }

    private sealed class FakeSource : HttpMessageHandler
    {
        public bool FailSecura = true;
        public int AnticaRequests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var url = request.RequestUri!;
            if (url.AbsolutePath == "/api/worlds") return Json("[\"Antica\",\"Secura\",\"RetiredWorld\"]");
            if (url.AbsolutePath == "/world_data") return Json("[{\"name\":\"Antica\"},{\"name\":\"Secura\"},{\"name\":\"NotInWiki\"}]");
            if (url.AbsolutePath == "/item_metadata") return Json("[{\"id\":3364,\"wiki_name\":\"Golden Legs\",\"name\":\"golden legs\"}]");
            if (url.Query.Contains("Antica")) AnticaRequests++;
            if (url.Query.Contains("Secura") && FailSecura) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Json("""
                [{"id":3364,"time":1792013978,"month_average_buy":35000,"month_bought":20,"month_average_sell":40000,"month_sold":10},
                {"id":999,"time":1792013978,"month_average_buy":-1,"month_bought":0,"month_average_sell":0,"month_sold":0}]
                """);
        }
        private static Task<HttpResponseMessage> Json(string body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private static WebApplicationFactory<Program> CreateApp(string cache, string basis = "buy") =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataRefresh:Enabled"] = "false", ["DataRefresh:CacheDirectory"] = cache,
                ["MarketSource:AverageBasis"] = basis,
                ["MarketSource:RequestIntervalSeconds"] = "0"
            })));
}
