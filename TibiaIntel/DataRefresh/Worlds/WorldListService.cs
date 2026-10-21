using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace TibiaIntel.DataRefresh.Worlds;

public sealed class WorldSourceOptions
{
    public string Url { get; set; } = "https://tibiawiki.dev/api/worlds";
    public string SourceKey => "worlds-v1|" + Url;
}

public sealed record WorldList(string[] Worlds);

// Shared cached world directory. Reads never trigger an external request.
public sealed class WorldListService(
    IHttpClientFactory clients,
    JsonSnapshotStore<WorldList> store,
    IOptionsMonitor<WorldSourceOptions> options,
    TimeProvider clock, UpstreamRequestPolicy requests) : IRefreshJob
{
    public string Name => "world-list";

    public async Task<DataSnapshot<WorldList>?> ReadAsync(CancellationToken token)
    {
        var snapshot = await store.ReadAsync(token);
        return snapshot?.SourceKey == options.CurrentValue.SourceKey ? snapshot : null;
    }

    public async Task<string[]> GetWorldsAsync(CancellationToken token) =>
        (await ReadAsync(token))?.Data.Worlds.ToArray() ?? [];

    public async Task<DateTimeOffset?> LastSuccessAsync(CancellationToken token) =>
        (await ReadAsync(token))?.UpdatedAt;

    public async Task RefreshAsync(CancellationToken token)
    {
        var configuration = options.CurrentValue;
        using var client = clients.CreateClient("world-list");
        using var response = await requests.GetAsync(client, configuration.Url, token);
        response.EnsureSuccessStatusCode();
        var rows = await response.Content.ReadFromJsonAsync<string[]>(token)
            ?? throw new InvalidDataException("The world source returned no list.");
        var worlds = rows.Where(w => !string.IsNullOrWhiteSpace(w) && w.All(char.IsAsciiLetter))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (worlds.Length == 0) throw new InvalidDataException("The world source returned no valid worlds.");
        if (configuration.SourceKey != options.CurrentValue.SourceKey)
            throw new InvalidOperationException("World source changed during refresh.");
        await store.WriteAsync(new(clock.GetUtcNow(), configuration.SourceKey, new(worlds)), token);
    }
}
