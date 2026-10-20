using System.Text.Json;
using Microsoft.Extensions.Options;
using TibiaHub.DataRefresh;
using TibiaHub.External.TibiaData;

namespace TibiaHub.CharacterScanning;

public sealed class OnlineWorldClient(HttpClient http, IOptions<TibiaDataOptions> options,
    UpstreamRequestPolicy requests, TimeProvider clock)
{
    private string Url(string path)
    {
        var uri = new Uri(new Uri(options.Value.BaseUrl), path);
        if (uri.Host.Equals("dev.tibiadata.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("All-world monitoring requires the production API; the development API quota is insufficient.");
        return uri.ToString();
    }

    public async Task<string[]> WorldsAsync(CancellationToken token)
    {
        using var response = await requests.GetAsync(http, Url("worlds"), token);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
        var worlds = json.RootElement.GetProperty("worlds");
        var names = new List<string>();
        foreach (var field in new[] { "regular_worlds", "tournament_worlds" })
            if (worlds.TryGetProperty(field, out var list) && list.ValueKind == JsonValueKind.Array)
                names.AddRange(list.EnumerateArray().Select(w => w.GetProperty("name").GetString()!));
        if (names.Count == 0 || names.Any(n => string.IsNullOrWhiteSpace(n) || !n.All(char.IsAsciiLetter)))
            throw new InvalidDataException("No valid active worlds returned.");
        return names.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<OnlineWorldSnapshot> FetchAsync(string world, CancellationToken token)
    {
        using var response = await requests.GetAsync(http, Url("world/" + Uri.EscapeDataString(world)), token);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
        var root = json.RootElement;
        var data = root.GetProperty("world");
        var name = data.GetProperty("name").GetString();
        if (!string.Equals(world, name, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("World name mismatch.");
        var sourceAt = root.GetProperty("information").GetProperty("timestamp").GetDateTimeOffset().ToUniversalTime();
        var fetched = clock.GetUtcNow();
        if (sourceAt > fetched.AddSeconds(5) || fetched - sourceAt > TimeSpan.FromMinutes(3))
            throw new InvalidDataException("World observation is stale or has a future timestamp.");
        var available = data.GetProperty("status").GetString() == "online";
        var names = data.TryGetProperty("online_players", out var players) && players.ValueKind == JsonValueKind.Array
            ? players.EnumerateArray().Select(p => p.GetProperty("name").GetString()!).ToArray()
            : available ? throw new InvalidDataException("Online player list is missing.") : [];
        if (names.Any(n => string.IsNullOrWhiteSpace(n) || n.Length > 100)
            || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length
            || (available && data.GetProperty("players_online").GetInt32() != names.Length))
            throw new InvalidDataException("Invalid or incomplete online player list.");
        return new(world, options.Value.BaseUrl, sourceAt, fetched, available, names);
    }
}
