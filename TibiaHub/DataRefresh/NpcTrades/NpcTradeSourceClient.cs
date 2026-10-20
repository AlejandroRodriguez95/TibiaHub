using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TibiaHub.DataRefresh.NpcTrades;

public sealed class NpcTradeSourceClient(HttpClient http, IOptionsMonitor<NpcTradeSourceOptions> options)
{
    public async Task<string> ValidateNpcAsync(string name, CancellationToken token)
    {
        var url = new Uri(new Uri(options.CurrentValue.TibiaWikiApiUrl), "api/npcs/" + Uri.EscapeDataString(name));
        using var json = await ReadAsync(url.ToString(), token);
        return json.RootElement.TryGetProperty("name", out var value) && value.GetString() is string canonical
            && canonical.Equals(name, StringComparison.OrdinalIgnoreCase)
            ? canonical : throw new InvalidDataException($"NPC '{name}' was not recognized by TibiaWiki.");
    }

    public async Task<string> PageAsync(string title, CancellationToken token)
    {
        var url = options.CurrentValue.WikiApiUrl + "?action=parse&prop=text&format=json&page=" + Uri.EscapeDataString(title);
        using var json = await ReadAsync(url, token);
        if (!json.RootElement.TryGetProperty("parse", out var page)
            || !page.TryGetProperty("text", out var text) || !text.TryGetProperty("*", out var html))
            throw new InvalidDataException($"Wiki page '{title}' did not contain parsed HTML.");
        return html.GetString() ?? throw new InvalidDataException("Empty wiki page.");
    }

    private async Task<JsonDocument> ReadAsync(string url, CancellationToken token)
    {
        // Bounded transient retries; honor upstream throttling without an unbounded wait.
        for (var attempt = 0; ; attempt++)
        {
            using var response = await http.GetAsync(url, token);
            if (attempt < 2 && ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500))
            {
                var delay = response.Headers.RetryAfter?.Delta
                    ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow)
                    ?? TimeSpan.FromSeconds(2 << attempt);
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 1, 60)), token);
                continue;
            }
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            return await JsonDocument.ParseAsync(stream, cancellationToken: token);
        }
    }
}
