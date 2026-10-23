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
using TibiaIntel.DataRefresh;
using Microsoft.Extensions.Time.Testing;
using TibiaIntel.DataRefresh.Market;
using TibiaIntel.DataRefresh.Worlds;
using TibiaIntel.DataRefresh.NpcTrades;

namespace TibiaIntel.Tests;

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
        var cache = Path.Combine(Path.GetTempPath(), "tibiaintel-market-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var app = CreateApp(cache);
            using var client = app.CreateClient();
            var services = app.Services;
            var key = services.GetRequiredService<IOptionsMonitor<MarketSourceOptions>>().CurrentValue.SourceKey;
            var worldKey = services.GetRequiredService<IOptionsMonitor<WorldSourceOptions>>().CurrentValue.SourceKey;
            await services.GetRequiredService<JsonSnapshotStore<WorldList>>().WriteAsync(new(DateTimeOffset.UtcNow, worldKey, new(["Antica", "Secura"])), default);
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

            var antica = await client.GetFromJsonAsync<JsonElement>("/api/tools/npc-selling?world=antica");
            var item = antica.GetProperty("npcs")[0].GetProperty("items")[0];
            Assert.Equal("Antica", antica.GetProperty("selectedWorld").GetString());
            Assert.True(antica.GetProperty("marketIsStale").GetBoolean());
            Assert.Equal(35000, item.GetProperty("averageMarketValue").GetInt64());
            Assert.True(item.GetProperty("isMarketHigher").GetBoolean());
            Assert.True(item.GetProperty("isDeliveryTask").GetBoolean());
            Assert.Equal(JsonValueKind.Null, antica.GetProperty("npcs")[0].GetProperty("items")[1].GetProperty("averageMarketValue").ValueKind);
            var secura = await client.GetFromJsonAsync<JsonElement>("/api/tools/npc-selling?world=Secura");
            Assert.False(secura.GetProperty("npcs")[0].GetProperty("items")[0].GetProperty("isMarketHigher").GetBoolean());
            Assert.False(secura.GetProperty("marketIsStale").GetBoolean());
            Assert.Equal(1, services.GetRequiredService<WorldRequestTracker>().Count("Antica"));
            Assert.Equal(1, services.GetRequiredService<WorldRequestTracker>().Count("Secura"));
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/tools/npc-selling?world=NotAWorld")).StatusCode);
            var unselected = await client.GetFromJsonAsync<JsonElement>("/api/tools/npc-selling");
            Assert.False(unselected.GetProperty("marketAvailable").GetBoolean());

            using var sellApp = CreateApp(cache, "sell");
            using var sellClient = sellApp.CreateClient();
            var sell = await sellClient.GetFromJsonAsync<JsonElement>("/api/tools/npc-selling?world=Secura");
            Assert.Equal(40000, sell.GetProperty("npcs")[0].GetProperty("items")[0].GetProperty("averageMarketValue").GetInt64());
        }
        finally { if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true); }
    }

    [Fact]
    public async Task CollectorCachesAllItemsAndResumesOnlyFailedWorlds()
    {
        var cache = Path.Combine(Path.GetTempPath(), "tibiaintel-market-test-" + Guid.NewGuid().ToString("N"));
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
            var worldKey = services.GetRequiredService<IOptionsMonitor<WorldSourceOptions>>().CurrentValue.SourceKey;
            await services.GetRequiredService<JsonSnapshotStore<WorldList>>().WriteAsync(new(DateTimeOffset.UtcNow, worldKey, new(["Antica", "Secura", "RetiredWorld"])), default);
            services.GetRequiredService<WorldRequestTracker>().Record("Secura"); // Failed first world must not block Antica.
            var job = new MarketRefreshJob(new(http, options, services.GetRequiredService<UpstreamRequestPolicy>()), services.GetRequiredService<WorldListService>(), worlds,
                services.GetRequiredService<JsonSnapshotStore<MarketItemIndex>>(), stores, services.GetRequiredService<WorldRequestTracker>(), options,
                TimeProvider.System, NullLogger<MarketRefreshJob>.Instance);
            await Assert.ThrowsAsync<InvalidOperationException>(() => job.RefreshAsync(default));
            Assert.Null(await job.LastSuccessAsync(default));
            Assert.Equal(["Secura", "Antica"], handler.PriceRequests);
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
            // An older unfinished cycle must not force unchanged successful worlds to download again.
            await worlds.WriteAsync(complete with { Data = complete.Data with
                { CycleStartedAt = DateTimeOffset.UtcNow.AddDays(-2), PendingWorlds = ["Secura"] } }, default);
            await job.RefreshAsync(default);
            Assert.Equal(1, handler.AnticaRequests);
            handler.AnticaUpdate = handler.AnticaUpdate.AddHours(1);
            await job.RefreshAsync(default);
            Assert.Equal(2, handler.AnticaRequests);
            Assert.Equal(handler.AnticaUpdate, (await stores.For("Antica").ReadAsync(default))!.Data.ProviderUpdatedAt);
        }
        finally { if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true); }
    }

    [Fact]
    public async Task WorldDirectoryIsIndependentCachedAndRetainedAfterFailure()
    {
        var cache = Path.Combine(Path.GetTempPath(), "tibiaintel-world-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var handler = new FakeSource())
            using (var app = CreateApp(cache).WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                services.AddHttpClient("world-list").ConfigurePrimaryHttpMessageHandler(() => handler))))
            {
                var directory = app.Services.GetRequiredService<WorldListService>();
                Assert.Empty(await directory.GetWorldsAsync(default));
                Assert.Equal(0, handler.WorldRequests);
                await directory.RefreshAsync(default);
                Assert.Equal(["Antica", "RetiredWorld", "Secura"], await directory.GetWorldsAsync(default));
                Assert.NotNull(await directory.LastSuccessAsync(default));
                Assert.Equal(1, handler.WorldRequests);
                Assert.Null(await app.Services.GetRequiredService<JsonSnapshotStore<MarketItemIndex>>().ReadAsync(default));
                Assert.Null(await app.Services.GetRequiredService<JsonSnapshotStore<MarketWorldList>>().ReadAsync(default));
                handler.FailWorlds = true;
                await Assert.ThrowsAsync<HttpRequestException>(() => directory.RefreshAsync(default));
                Assert.Equal(["Antica", "RetiredWorld", "Secura"], await directory.GetWorldsAsync(default));
                Assert.Equal(4, handler.WorldRequests);
            }
            using var restarted = CreateApp(cache);
            Assert.Equal(["Antica", "RetiredWorld", "Secura"],
                await restarted.Services.GetRequiredService<WorldListService>().GetWorldsAsync(default));
        }
        finally { if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueueUsesPopularityThenNewestScanAndSkipsUnchangedWorlds(bool preferSecura)
    {
        var cache = Path.Combine(Path.GetTempPath(), "tibiaintel-priority-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            DateTimeOffset originalCollection;
            using (var app = CreateApp(cache))
            using (var handler = new FakeSource { FailSecura = false })
            using (var http = new HttpClient(handler))
            {
                await SeedDirectory(app.Services);
                var tracker = app.Services.GetRequiredService<WorldRequestTracker>();
                if (preferSecura) { tracker.Record("Secura"); tracker.Record("secura"); }
                var job = CreateJob(app.Services, http);
                await job.RefreshAsync(default);
                Assert.Equal(preferSecura ? ["Secura", "Antica"] : new[] { "Antica", "Secura" }, handler.PriceRequests);
                var antica = app.Services.GetRequiredService<MarketWorldStore>().For("Antica");
                originalCollection = (await antica.ReadAsync(default))!.UpdatedAt;
                handler.PriceRequests.Clear();
                // Even very popular worlds are skipped if their scan is unchanged.
                for (var i = 0; i < 10; i++) tracker.Record("Antica");
                await job.RefreshAsync(default);
                Assert.Empty(handler.PriceRequests);
                Assert.Equal(originalCollection, (await antica.ReadAsync(default))!.UpdatedAt);
                handler.SecuraUpdate = handler.SecuraUpdate.AddHours(2);
                await job.RefreshAsync(default);
                Assert.Equal(["Secura"], handler.PriceRequests);
                Assert.NotNull(await job.LastSuccessAsync(default));
            }
            // Persisted scan markers also suppress duplicate downloads after restart.
            using var restarted = CreateApp(cache);
            using var restartedHandler = new FakeSource { FailSecura = false, SecuraUpdate = DateTimeOffset.Parse("2026-10-20T11:00:00Z") };
            using var restartedHttp = new HttpClient(restartedHandler);
            await CreateJob(restarted.Services, restartedHttp).RefreshAsync(default);
            Assert.Empty(restartedHandler.PriceRequests);
            Assert.Equal(originalCollection, (await restarted.Services.GetRequiredService<MarketWorldStore>().For("Antica").ReadAsync(default))!.UpdatedAt);
        }
        finally { if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true); }
    }

    [Fact]
    public async Task QueueReordersRemainingWorldsWhenNewDemandArrives()
    {
        var cache = Path.Combine(Path.GetTempPath(), "tibiaintel-priority-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var app = CreateApp(cache);
            await SeedDirectory(app.Services, "Antica", "Secura", "Premia");
            var tracker = app.Services.GetRequiredService<WorldRequestTracker>();
            using var handler = new FakeSource { FailSecura = false, IncludePremia = true,
                OnPriceRequest = world => { if (world == "Antica") tracker.Record("Premia"); } };
            using var http = new HttpClient(handler);
            await CreateJob(app.Services, http).RefreshAsync(default);
            Assert.Equal(["Antica", "Premia", "Secura"], handler.PriceRequests);
        }
        finally { if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true); }
    }

    [Fact]
    public async Task LegacyCacheWithoutScanMarkerIsFetchedOnceAndOlderProviderScansAreSkipped()
    {
        var cache = Path.Combine(Path.GetTempPath(), "tibiaintel-scan-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var app = CreateApp(cache);
            await SeedDirectory(app.Services, "Antica");
            var key = app.Services.GetRequiredService<IOptionsMonitor<MarketSourceOptions>>().CurrentValue.SourceKey;
            var store = app.Services.GetRequiredService<MarketWorldStore>().For("Antica");
            await store.WriteAsync(new(DateTimeOffset.UtcNow, key, new("Antica", [new(3364, 10, 20, DateTimeOffset.UtcNow)])), default);
            using var handler = new FakeSource { FailSecura = false };
            using var http = new HttpClient(handler);
            var job = CreateJob(app.Services, http);
            await job.RefreshAsync(default);
            Assert.Equal(["Antica"], handler.PriceRequests);
            handler.AnticaUpdate = handler.AnticaUpdate.AddDays(-1);
            await job.RefreshAsync(default);
            Assert.Single(handler.PriceRequests);
        }
        finally { if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true); }
    }

    private static async Task SeedDirectory(IServiceProvider services, params string[] worlds)
    {
        var key = services.GetRequiredService<IOptionsMonitor<WorldSourceOptions>>().CurrentValue.SourceKey;
        await services.GetRequiredService<JsonSnapshotStore<WorldList>>().WriteAsync(
            new(DateTimeOffset.UtcNow, key, new(worlds.Length == 0 ? ["Antica", "Secura"] : worlds)), default);
    }

    private static MarketRefreshJob CreateJob(IServiceProvider services, HttpClient http) => new(
        new(http, services.GetRequiredService<IOptionsMonitor<MarketSourceOptions>>(), services.GetRequiredService<UpstreamRequestPolicy>()),
        services.GetRequiredService<WorldListService>(), services.GetRequiredService<JsonSnapshotStore<MarketWorldList>>(),
        services.GetRequiredService<JsonSnapshotStore<MarketItemIndex>>(), services.GetRequiredService<MarketWorldStore>(),
        services.GetRequiredService<WorldRequestTracker>(), services.GetRequiredService<IOptionsMonitor<MarketSourceOptions>>(),
        TimeProvider.System, NullLogger<MarketRefreshJob>.Instance);

    private sealed class FakeSource : HttpMessageHandler
    {
        public bool FailSecura = true;
        public bool FailWorlds;
        public bool IncludePremia;
        public int WorldRequests;
        public int AnticaRequests;
        public DateTimeOffset AnticaUpdate = DateTimeOffset.Parse("2026-10-20T10:00:00Z");
        public DateTimeOffset SecuraUpdate = DateTimeOffset.Parse("2026-10-20T09:00:00Z");
        public List<string> PriceRequests { get; } = [];
        public Action<string>? OnPriceRequest;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var url = request.RequestUri!;
            if (url.AbsolutePath == "/api/worlds")
            {
                WorldRequests++;
                if (FailWorlds) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                return Json("[\"Antica\",\"Secura\",\"RetiredWorld\"]");
            }
            if (url.AbsolutePath == "/world_data")
            {
                var statuses = new List<object> { new { name = "Antica", last_update = AnticaUpdate },
                    new { name = "Secura", last_update = SecuraUpdate }, new { name = "NotInWiki", last_update = AnticaUpdate } };
                if (IncludePremia) statuses.Add(new { name = "Premia", last_update = SecuraUpdate.AddHours(-1) });
                return Json(JsonSerializer.Serialize(statuses));
            }
            if (url.AbsolutePath == "/item_metadata") return Json("[{\"id\":3364,\"wiki_name\":\"Golden Legs\",\"name\":\"golden legs\"}]");
            var world = url.Query.Contains("Antica") ? "Antica" : url.Query.Contains("Premia") ? "Premia" : "Secura";
            PriceRequests.Add(world);
            OnPriceRequest?.Invoke(world);
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
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(
                new FakeTimeProvider(DateTimeOffset.UtcNow) { AutoAdvanceAmount = TimeSpan.FromMinutes(1) }));
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataRefresh:Enabled"] = "false", ["DataRefresh:CacheDirectory"] = cache,
                ["MarketSource:AverageBasis"] = basis,
                ["MarketSource:RequestIntervalSeconds"] = "0"
            }));
        });
}
