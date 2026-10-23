using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TibiaIntel.DataRefresh;
using Testcontainers.PostgreSql;
using TibiaIntel.CharacterScanning;
using TibiaIntel.Data;
using TibiaIntel.Vip;

namespace TibiaIntel.Tests;

public sealed class VipFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:17").Build();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = database.GetConnectionString(),
            ["DataRefresh:Enabled"] = "false", ["Vip:MaxCharactersPerUser"] = "2", ["Vip:MaxGroupsPerUser"] = "3"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IVipCharacterResolver>();
            services.AddSingleton<IVipCharacterResolver, FakeVipResolver>();
            // Feed presence fixtures through the real reader while keeping upstream workers disabled.
            services.AddScoped(sp => new VipService(sp.GetRequiredService<ApplicationDbContext>(),
                sp.GetRequiredService<IVipCharacterResolver>(), sp.GetRequiredService<IOptionsMonitor<VipOptions>>(),
                sp.GetRequiredService<IOptionsMonitor<CharacterScanOptions>>(),
                new VipReadOptions<RefreshOptions>(new() { Enabled = true }), TimeProvider.System));
        });
    }
    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    }
    async Task IAsyncLifetime.DisposeAsync() { await DisposeAsync(); await database.DisposeAsync(); }

    public sealed class FakeVipResolver : IVipCharacterResolver
    {
        public Task<VipIdentity> ResolveAsync(string name, CancellationToken token) =>
            name.Equals("Missing", StringComparison.OrdinalIgnoreCase) ? throw new VipException(404, "Character not found.")
            : Task.FromResult(new VipIdentity(string.Join(' ', name.Split(' ').Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant())), "Antica"));
    }
}

public sealed class VipReadOptions<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

public sealed class VipTests(VipFixture fixture) : IClassFixture<VipFixture>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.WorldObservations.ExecuteDeleteAsync();
        await db.OnlineSessions.ExecuteDeleteAsync();
        await db.ScannedCharacters.ExecuteDeleteAsync();
    }
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(HttpClient Client, string Cookie)> Account()
    {
        var client = fixture.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await RegistrationFixture.RefreshCsrfAsync(client);
        var credentials = new { email = Guid.NewGuid() + "@example.com", password = "ValidPassword123!" };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/account/register", credentials)).StatusCode);
        var login = await client.PostAsJsonAsync("/account/login", credentials);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));
        await RegistrationFixture.RefreshCsrfAsync(client);
        return (client, cookie);
    }
    private static async Task<VipSnapshot> Read(HttpClient client) => (await client.GetFromJsonAsync<VipSnapshot>("/tools/vip"))!;
    private static async Task Add(HttpClient client, string name) =>
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/tools/vip/characters", new { name })).StatusCode);
    private static async Task<long> Group(HttpClient client, string name)
    {
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/tools/vip/groups", new { name })).StatusCode);
        return (await Read(client)).Groups.Single(g => g.Name == name).Id;
    }

    [Fact]
    public async Task AnonymousAccessAndCrossOriginHubAreRejectedAndWritesRequireCsrf()
    {
        using var anon = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/tools/vip")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsync("/hubs/vip/negotiate?negotiateVersion=1", null)).StatusCode);
        var (client, _) = await Account();
        using (client)
        {
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/tools/vip/characters", new { name = "Bob" })).StatusCode);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/hubs/vip/negotiate?negotiateVersion=1");
            request.Headers.Add("Origin", "https://untrusted.example");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
        }
    }

    [Fact]
    public async Task MultiGroupMembershipIsPrivateAndDeletingGroupKeepsCharacters()
    {
        var (client, _) = await Account(); using var owner = client;
        var (otherClient, _) = await Account(); using var other = otherClient;
        await Add(owner, "Lazy Snorlax"); await Add(owner, "Krieghor");
        var first = await Group(owner, "Alejandro"); var second = await Group(owner, "Friends");
        var entry = (await Read(owner)).Characters.Single(c => c.Name == "Lazy Snorlax");
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"/tools/vip/characters/{entry.Id}/groups", new { groupIds = new[] { first, second, first } })).StatusCode);
        var snapshot = await Read(owner);
        Assert.Equal(2, snapshot.Characters.Length); Assert.Equal(2, snapshot.MaxCharacters);
        Assert.Equal(new[] { first, second }, snapshot.Characters.Single(c => c.Id == entry.Id).GroupIds);
        Assert.All(snapshot.Characters, c => Assert.Equal("unknown", c.Status));
        Assert.Empty((await Read(other)).Characters);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/tools/vip/characters/{entry.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/tools/vip/groups/{first}", new { name = "Hijacked" })).StatusCode);
        await Add(other, "Bob"); var foreign = (await Read(other)).Characters.Single();
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/tools/vip/characters/{foreign.Id}/groups", new { groupIds = new[] { first } })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"/tools/vip/groups/{first}", new { name = "Renamed" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/tools/vip/groups/{first}")).StatusCode);
        Assert.Equal(new[] { second }, (await Read(owner)).Characters.Single(c => c.Id == entry.Id).GroupIds);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/tools/vip/groups/{second}")).StatusCode);
        Assert.Equal(2, (await Read(owner)).Characters.Length);
        Assert.All((await Read(owner)).Characters, c => Assert.Empty(c.GroupIds));
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/tools/vip/characters/{entry.Id}")).StatusCode);
        Assert.Single((await Read(owner)).Characters);
    }

    [Fact]
    public async Task InvalidCharactersDuplicatesAndConcurrentAddsRespectConfigurableLimit()
    {
        var (client, _) = await Account(); using var owner = client;
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync("/tools/vip/characters", new { name = "Missing" })).StatusCode);
        await Add(owner, "Lazy Snorlax");
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync("/tools/vip/characters", new { name = "lazy snorlax" })).StatusCode);
        var responses = await Task.WhenAll(owner.PostAsJsonAsync("/tools/vip/characters", new { name = "Krieghor" }),
            owner.PostAsJsonAsync("/tools/vip/characters", new { name = "Bob" }));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.BadRequest);
        Assert.Equal(2, (await Read(owner)).Characters.Length);
        for (var i = 0; i < 3; i++) await Group(owner, "Group " + i);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/tools/vip/groups", new { name = "Over limit" })).StatusCode);
    }

    [Theory]
    [InlineData(0, "", true)]
    [InlineData(121, "", false)]
    [InlineData(0, "fetch-failed", false)]
    [InlineData(0, "world-offline", false)]
    [InlineData(0, "mass-change", true)]
    [InlineData(0, "server-save", true)]
    public void PresenceFreshnessDoesNotConfuseScanScoringExclusions(int age, string exclusion, bool expected)
    {
        var now = DateTimeOffset.UtcNow;
        var observation = new WorldObservation { SourceAt = now.AddSeconds(-age), FetchedAt = now, Exclusion = exclusion };
        Assert.Equal(expected, VipService.IsFresh(observation, now, 120));
        Assert.False(VipService.IsFresh(null, now, 120));
    }

    [Fact]
    public async Task StatusReadsOpenSessionsAndLatestObservationFromSameWorld()
    {
        var (client, _) = await Account(); using var owner = client;
        await Add(owner, "Lazy Snorlax"); await Add(owner, "Krieghor");
        var user = await owner.GetFromJsonAsync<JsonElement>("/account/myaccount");
        var userId = user.GetProperty("id").GetString()!;
        async Task<VipSnapshot> ReadEnabled()
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var reader = new VipService(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
                scope.ServiceProvider.GetRequiredService<IVipCharacterResolver>(), new ConstantOptions<VipOptions>(new()),
                new ConstantOptions<CharacterScanOptions>(new()), new ConstantOptions<RefreshOptions>(new() { Enabled = true }), TimeProvider.System);
            return await reader.ReadAsync(userId, default);
        }
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.WorldObservations.ExecuteDeleteAsync();
            await db.OnlineSessions.ExecuteDeleteAsync();
            await db.ScannedCharacters.ExecuteDeleteAsync();
            var now = DateTimeOffset.UtcNow;
            db.WorldObservations.Add(new() { World = "Antica", SourceAt = now, FetchedAt = now });
            db.OnlineSessions.Add(new() { Character = new() { World = "Antica", Name = "Lazy Snorlax", NormalizedName = "LAZY SNORLAX" }, LoginAt = now });
            await db.SaveChangesAsync();
        }
        var snapshot = await ReadEnabled();
        Assert.Equal("online", snapshot.Characters.Single(c => c.Name == "Lazy Snorlax").Status);
        Assert.Equal("offline", snapshot.Characters.Single(c => c.Name == "Krieghor").Status);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.WorldObservations.Add(new() { World = "Antica", FetchedAt = DateTimeOffset.UtcNow, Exclusion = "fetch-failed" });
            await db.SaveChangesAsync();
        }
        Assert.All((await ReadEnabled()).Characters, c => Assert.Equal("unknown", c.Status));
    }

    private sealed class ConstantOptions<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    [Fact]
    public async Task HubUsesAuthenticatedAccountAndBroadcastsEditsThenCleansUpDisconnect()
    {
        var (client, cookie) = await Account(); using var owner = client;
        await Add(owner, "Lazy Snorlax");
        await using var connection = new HubConnectionBuilder().WithUrl("https://localhost/hubs/vip", options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => fixture.Server.CreateHandler();
            options.Headers["Cookie"] = cookie;
        }).Build();
        var first = new TaskCompletionSource<VipSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var changed = new TaskCompletionSource<VipSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var online = new TaskCompletionSource<VipStatusUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        var offline = new TaskCompletionSource<VipStatusUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<VipStatusUpdate>("Statuses", value =>
        {
            if (value.Characters.Any(c => c.Status == "online")) online.TrySetResult(value);
            if (value.Characters.Any(c => c.Status == "offline")) offline.TrySetResult(value);
        });
        connection.On<VipSnapshot>("Snapshot", value => { if (value.Groups.Length == 0) first.TrySetResult(value); else changed.TrySetResult(value); });
        await connection.StartAsync();
        var initial = await connection.InvokeAsync<VipSnapshot>("Snapshot");
        Assert.Equal("Lazy Snorlax", Assert.Single(initial.Characters).Name);
        await first.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await Group(owner, "Alejandro");
        var pushed = await changed.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal("Alejandro", Assert.Single(pushed.Groups).Name);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.WorldObservations.Add(new() { World = "Antica", SourceAt = now, FetchedAt = now });
            db.OnlineSessions.Add(new() { Character = new() { World = "Antica", Name = "Lazy Snorlax", NormalizedName = "LAZY SNORLAX" }, LoginAt = now });
            await db.SaveChangesAsync();
        }
        Assert.Equal("online", Assert.Single((await online.Task.WaitAsync(TimeSpan.FromSeconds(15))).Characters).Status);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.OnlineSessions.Where(s => s.LogoutAt == null).ExecuteUpdateAsync(s => s.SetProperty(v => v.LogoutAt, DateTimeOffset.UtcNow));
        }
        Assert.Equal("offline", Assert.Single((await offline.Task.WaitAsync(TimeSpan.FromSeconds(15))).Characters).Status);
        await connection.StopAsync();
        Assert.Empty(fixture.Services.GetRequiredService<VipConnections>().Users);
    }
}
