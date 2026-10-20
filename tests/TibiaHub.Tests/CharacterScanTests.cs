using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using TibiaHub.DataRefresh;
using TibiaHub.DataRefresh.Market;
using TibiaHub.External.TibiaData;
using Microsoft.Extensions.Time.Testing;
using TibiaHub.CharacterScanning;
using TibiaHub.Data;

namespace TibiaHub.Tests;

public sealed class CharacterScanTests(RegistrationFixture fixture) : IClassFixture<RegistrationFixture>, IAsyncLifetime
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-20T00:00:00Z");

    public async Task InitializeAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.OnlineSessions.ExecuteDeleteAsync();
        await db.ScannedCharacters.ExecuteDeleteAsync();
        await db.WorldObservations.ExecuteDeleteAsync();
        await fixture.ResetUsersAsync();
    }
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task Observe(int seconds, string[] names, string world = "Antica", bool available = true)
    {
        // A new scope every time simulates state recovery without a long-lived EF context.
        await using var scope = fixture.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SessionRecorder>().RecordAsync(
            new(world, "test-source", Start.AddSeconds(seconds), Start.AddSeconds(seconds + 1), available, names), default);
    }

    private async Task<CharacterScanResult> Scan(string world = "Antica", string name = "Bob")
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return await new CharacterScanService(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            Options.Create(new CharacterScanOptions()), new FakeTimeProvider(Start.AddDays(1))).ScanAsync(world, name, default);
    }

    [Fact]
    public async Task SessionsRecordOnlyChangesRecoverAcrossScopesAndIgnoreRepeatedSources()
    {
        await Observe(0, ["Bob", "Alice"]);
        await Observe(60, ["Bob", "Alice", "Charlie"]);
        await Observe(120, ["Alice", "Charlie", "Dave"]);
        await Observe(120, ["Wrong cached duplicate"]);
        await Observe(180, ["Alice", "Charlie", "Dave"]);
        await Observe(240, ["Alice", "Charlie", "Dave"]);
        var result = await Scan(name: " bob ");
        Assert.Equal(1, result.ObservedLogouts);
        Assert.Equal(1, result.UsableLogouts);
        Assert.Equal(new ScanCandidate("Dave", 10, 1, 0), Assert.Single(result.Candidates));
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(4, await db.OnlineSessions.CountAsync());
        Assert.Equal(5, await db.WorldObservations.CountAsync());
        Assert.False((await db.OnlineSessions.Include(s => s.Character).SingleAsync(s => s.Character.Name == "Bob")).LoginReliable);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 10)]
    [InlineData(60, 10)]
    [InlineData(61, 5)]
    [InlineData(120, 5)]
    [InlineData(121, 0)]
    public async Task ScoringUsesExactMinuteBoundariesAndNeverMatchesOtherWorlds(int offset, int expected)
    {
        await Observe(0, ["Bob"]);
        await Observe(60, ["Bob"]);
        await Observe(120, []);
        await Observe(180, []);
        await Observe(240, []);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreach (var world in new[] { "Antica", "Secura" })
                db.OnlineSessions.Add(new() { Character = new() { World = world, Name = "Dave", NormalizedName = "DAVE" },
                    LoginAt = Start.AddSeconds(120 + offset), LoginReliable = true });
            await db.SaveChangesAsync();
        }
        var result = await Scan();
        Assert.Equal(1, result.UsableLogouts);
        Assert.Equal(expected, result.Candidates.Sum(c => c.Score));
        Assert.True(result.Candidates.Count <= 1);
    }

    [Fact]
    public async Task RepeatedCandidateLoginsReceiveOnlyTheHighestAwardForEachLogout()
    {
        await Observe(0, ["Bob"]);
        await Observe(60, ["Bob"]);
        await Observe(120, ["Dave"]);
        await Observe(150, []);
        await Observe(180, []);
        await Observe(240, ["Dave"]);
        var result = await Scan();
        Assert.Equal(new ScanCandidate("Dave", 10, 1, 0), Assert.Single(result.Candidates));
    }

    [Fact]
    public async Task FailedFetchAndIncompleteWindowsCannotEarnPoints()
    {
        await Observe(0, ["Bob"]);
        await Observe(60, []);
        Assert.Equal(0, (await Scan()).UsableLogouts); // Follow-up history has not arrived.
        await using (var scope = fixture.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<SessionRecorder>().RecordFailureAsync("Antica", Start.AddSeconds(80), default);
        await Observe(120, ["Dave"]);
        await Observe(180, ["Dave"]);
        var result = await Scan();
        Assert.Equal(0, result.UsableLogouts);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task GapsAndOfflineResponsesProduceUncertainBoundaries()
    {
        await Observe(0, ["Bob"]);
        await Observe(180, ["Dave"]);
        await Observe(240, [], available: false);
        await Observe(300, ["Bob"]);
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.All(await db.OnlineSessions.ToArrayAsync(), s => Assert.False(s.LoginReliable));
        Assert.Contains(await db.WorldObservations.ToArrayAsync(), o => o.Exclusion == "collection-gap");
        Assert.Contains(await db.WorldObservations.ToArrayAsync(), o => o.Exclusion == "world-offline");
        Assert.Empty((await Scan()).Candidates);
    }

    [Fact]
    public async Task MassDisconnectAndReconnectAreExcluded()
    {
        var crowd = Enumerable.Range(0, 50).Select(i => "Player " + i).ToArray();
        await Observe(0, crowd);
        await Observe(60, crowd);
        await Observe(120, ["Replacement"]);
        await Observe(180, crowd);
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await db.WorldObservations.CountAsync(o => o.Exclusion == "mass-change"));
        Assert.False((await db.OnlineSessions.Include(s => s.Character).FirstAsync(s => s.Character.Name == "Player 0")).LogoutReliable);
        Assert.Empty((await Scan(name: "Player 0")).Candidates);
    }

    [Fact]
    public async Task RetentionPrunesOldSessionsWithoutDeletingTheCharactersRecentHistory()
    {
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var character = new ScannedCharacter { World = "Antica", Name = "Bob", NormalizedName = "BOB" };
            db.OnlineSessions.AddRange(
                new OnlineSession { Character = character, LoginAt = Start, LogoutAt = Start.AddHours(2) },
                new OnlineSession { Character = character, LoginAt = Start.AddDays(19) });
            await db.SaveChangesAsync();
        }
        await using (var scope = fixture.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<SessionRecorder>().PruneAsync(Start.AddDays(20), default);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal("Bob", (await db.ScannedCharacters.SingleAsync()).Name);
            var remaining = await db.OnlineSessions.SingleAsync();
            Assert.Equal(Start.AddDays(19), remaining.LoginAt);
            Assert.Null(remaining.LogoutAt);
        }
    }

    [Fact]
    public async Task RetentionDeletesOldClosedSessionsButKeepsOpenAndOverlappingSessions()
    {
        var now = Start.AddDays(20);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreach (var (name, end) in new[] { ("Old", (DateTimeOffset?)Start.AddDays(1)), ("Overlap", Start.AddDays(7)), ("Open", null) })
                db.OnlineSessions.Add(new() { Character = new() { World = "Antica", Name = name, NormalizedName = name.ToUpperInvariant() }, LoginAt = Start, LogoutAt = end });
            db.WorldObservations.AddRange(new WorldObservation { World = "Antica", SourceAt = Start, FetchedAt = Start },
                new WorldObservation { World = "Antica", SourceAt = Start.AddDays(2), FetchedAt = Start.AddDays(2) });
            await db.SaveChangesAsync();
        }
        await using (var scope = fixture.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<SessionRecorder>().PruneAsync(now, default);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(2, await db.OnlineSessions.CountAsync());
            Assert.Equal(2, await db.ScannedCharacters.CountAsync());
            Assert.Equal(Start.AddDays(2), (await db.WorldObservations.SingleAsync()).SourceAt);
        }
    }

    [Theory]
    [InlineData("2026-07-01T07:54:00Z", "2026-07-01T07:56:00Z", true)]
    [InlineData("2026-01-01T08:54:00Z", "2026-01-01T08:56:00Z", true)]
    [InlineData("2026-07-01T06:00:00Z", "2026-07-01T06:01:00Z", false)]
    public void ServerSaveExclusionUsesLocalTimeIncludingDaylightSaving(string from, string to, bool excluded) =>
        Assert.Equal(excluded, SessionRecorder.OverlapsServerSave(DateTimeOffset.Parse(from), DateTimeOffset.Parse(to), new()));

    [Fact]
    public async Task CollectorAttemptsEveryDiscoveredWorldEvenWhenOneFetchFails()
    {
        using var handler = new CollectorSource();
        using var http = new HttpClient(handler);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow) { AutoAdvanceAmount = TimeSpan.FromSeconds(1) };
        var source = new OnlineWorldClient(http, Options.Create(new TibiaDataOptions()),
            new UpstreamRequestPolicy(clock, fixture.Services.GetRequiredService<IOptionsMonitor<MarketSourceOptions>>()), TimeProvider.System);
        using var collector = new OnlineCollector(fixture.Services.GetRequiredService<IServiceScopeFactory>(), source,
            new FixedOptions<CharacterScanOptions>(new()), new FixedOptions<RefreshOptions>(new() { Enabled = true }),
            TimeProvider.System, NullLogger<OnlineCollector>.Instance);
        await collector.StartAsync(default);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                await using var scope = fixture.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                if (await db.WorldObservations.Select(o => o.World).Distinct().CountAsync(deadline.Token) == 2) break;
                await Task.Delay(20, deadline.Token);
            }
        }
        finally { await collector.StopAsync(default); }
        await using var check = fixture.Services.CreateAsyncScope();
        var database = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal("fetch-failed", (await database.WorldObservations.SingleAsync(o => o.World == "Antica")).Exclusion);
        Assert.Equal("initial-baseline", (await database.WorldObservations.SingleAsync(o => o.World == "Secura")).Exclusion);
        Assert.Equal("Bob", (await database.ScannedCharacters.SingleAsync()).Name);
    }

    private sealed class FixedOptions<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class CollectorSource : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/Antica")) throw new HttpRequestException("Simulated world failure");
            var json = path.EndsWith("/worlds")
                ? "{\"worlds\":{\"regular_worlds\":[{\"name\":\"Antica\"},{\"name\":\"Secura\"}]}}"
                : JsonSerializer.Serialize(new { information = new { timestamp = DateTimeOffset.UtcNow },
                    world = new { name = "Secura", status = "online", players_online = 1, online_players = new[] { new { name = "Bob" } } } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }

    [Fact]
    public async Task ScanEndpointsRequireLoginAndUseOnlyRecordedHistory()
    {
        using var client = await fixture.CreateAccountClientAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/tools/character-scan?name=Bob")).StatusCode);
        var credentials = new { email = "scanner@example.com", password = "ValidPassword123!" };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/account/register", credentials)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/account/login", credentials)).StatusCode);
        await Observe(0, ["Bob"]);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/tools/character-scan?name=Unknown")).StatusCode);
        var result = await client.GetFromJsonAsync<JsonElement>("/tools/character-scan?name=Bob");
        Assert.Equal(0, result.GetProperty("usableLogouts").GetInt32());
        Assert.Equal("Antica", result.GetProperty("world").GetString());
        await Observe(60, ["Bob"], world: "Secura");
        var transferred = await client.GetFromJsonAsync<JsonElement>("/tools/character-scan?name=bob");
        Assert.Equal("Secura", transferred.GetProperty("world").GetString());
    }
}
