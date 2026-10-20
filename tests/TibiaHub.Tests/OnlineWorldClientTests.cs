using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TibiaHub.CharacterScanning;
using TibiaHub.DataRefresh;
using TibiaHub.DataRefresh.Market;
using TibiaHub.External.TibiaData;

namespace TibiaHub.Tests;

public sealed class OnlineWorldClientTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-21T00:01:00Z");

    [Fact]
    public async Task DiscoversAllActiveWorldsIncludingTournamentWorlds()
    {
        using var handler = new JsonSource("""
            {"worlds":{"regular_worlds":[{"name":"Antica"},{"name":"Secura"}],"tournament_worlds":[{"name":"Tournament"}]}}
            """);
        using var http = new HttpClient(handler);
        Assert.Equal(["Antica", "Secura", "Tournament"], await CreateClient(http).WorldsAsync(default));
        Assert.EndsWith("/v4/worlds", handler.LastUrl);
    }

    [Fact]
    public async Task FreshObservationPreservesSourceAndFetchTimes()
    {
        using var handler = new JsonSource("""
            {"information":{"timestamp":"2026-10-21T00:00:30Z"},"world":{"name":"Antica","status":"online","players_online":1,"online_players":[{"name":"Bob"}]}}
            """);
        using var http = new HttpClient(handler);
        var snapshot = await CreateClient(http).FetchAsync("Antica", default);
        Assert.Equal(Now.AddSeconds(-30), snapshot.SourceAt);
        Assert.Equal(Now, snapshot.FetchedAt);
        Assert.Equal(["Bob"], snapshot.Names);
        Assert.True(snapshot.Available);
    }

    [Theory]
    [InlineData("2026-10-20T00:00:00Z", "Antica", 1)]
    [InlineData("2026-10-21T01:00:00Z", "Antica", 1)]
    [InlineData("2026-10-21T00:00:30Z", "Secura", 1)]
    [InlineData("2026-10-21T00:00:30Z", "Antica", 2)]
    public async Task InvalidOrStaleResponsesCannotBecomeEmptyOnlineLists(string timestamp, string world, int count)
    {
        using var handler = new JsonSource(System.Text.Json.JsonSerializer.Serialize(new {
            information = new { timestamp }, world = new { name = world, status = "online", players_online = count, online_players = new[] { new { name = "Bob" } } }
        }));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => CreateClient(http).FetchAsync("Antica", default));
    }

    [Fact]
    public async Task MonitoringCannotRunAgainstTheLowQuotaDevelopmentApi()
    {
        using var handler = new JsonSource("{}");
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateClient(http, "https://dev.tibiadata.com/v4/").WorldsAsync(default));
        Assert.Null(handler.LastUrl);
    }

    private static OnlineWorldClient CreateClient(HttpClient http, string baseUrl = "https://api.tibiadata.com/v4/")
    {
        var clock = new FakeTimeProvider(Now);
        using var services = new ServiceCollection().AddOptions().Configure<MarketSourceOptions>(_ => { }).BuildServiceProvider();
        return new(http, Options.Create(new TibiaDataOptions { BaseUrl = baseUrl }),
            new UpstreamRequestPolicy(clock, services.GetRequiredService<IOptionsMonitor<MarketSourceOptions>>()), clock);
    }

    private sealed class JsonSource(string json) : HttpMessageHandler
    {
        public string? LastUrl;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            LastUrl = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
