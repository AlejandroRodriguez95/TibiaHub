using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TibiaHub.DataRefresh.Market;

public sealed class MarketSourceClient(HttpClient http, IOptionsMonitor<MarketSourceOptions> options, TimeProvider clock)
{
    private readonly SemaphoreSlim requestGate = new(1, 1);
    private DateTimeOffset nextRequest = DateTimeOffset.MinValue;
    public async Task<string[]> WorldsAsync(CancellationToken token)
    {
        using var wiki = await ReadAsync(options.CurrentValue.WorldsUrl, token);
        using var market = await ReadAsync(Url("world_data"), token);
        var wikiNames = wiki.RootElement.EnumerateArray().Select(w => w.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = market.RootElement.EnumerateArray().Select(w => w.GetProperty("name").GetString()!)
            .Where(w => wikiNames.Contains(w) && w.All(char.IsAsciiLetter)).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
        if (names.Length == 0) throw new InvalidDataException("No supported wiki worlds were found.");
        return names;
    }

    public async Task<MarketItemIndex> ItemsAsync(CancellationToken token)
    {
        // This endpoint returns all metadata, with no pagination in the API contract.
        using var json = await ReadAsync(Url("item_metadata"), token);
        var items = json.RootElement.EnumerateArray().Select(item => new MarketItemMetadata(
            item.GetProperty("id").GetInt32(), String(item, "wiki_name"), String(item, "name"))).ToArray();
        if (items.Length == 0 || items.Any(i => i.Id <= 0) || items.Select(i => i.Id).Distinct().Count() != items.Length)
            throw new InvalidDataException("Invalid market item metadata.");
        return new(items);
    }

    public async Task<MarketWorldPrices> PricesAsync(string world, CancellationToken token)
    {
        const int pageSize = 10000;
        var result = new Dictionary<int, MarketItemPrice>();
        for (var skip = 0; ; skip += pageSize)
        {
            if (skip >= 100000) throw new InvalidDataException("Market feed exceeded the pagination safety limit.");
            using var json = await ReadAsync(Url($"market_values?server={Uri.EscapeDataString(world)}&skip={skip}&limit={pageSize}"), token);
            var rows = json.RootElement.EnumerateArray().ToArray();
            foreach (var item in rows)
            {
                var id = item.GetProperty("id").GetInt32();
                var time = item.GetProperty("time").GetDouble();
                if (id <= 0 || !double.IsFinite(time)) throw new InvalidDataException("Invalid market item ID or source timestamp.");
                var row = new MarketItemPrice(id, Average(item, "month_average_buy", "month_bought"),
                    Average(item, "month_average_sell", "month_sold"), DateTimeOffset.FromUnixTimeSeconds(checked((long)time)));
                if (!result.TryAdd(id, row)) throw new InvalidDataException("Duplicate market item ID; pagination may have changed.");
            }
            if (rows.Length < pageSize) break;
        }
        if (result.Count == 0) throw new InvalidDataException($"No market rows were returned for {world}; previous data is retained.");
        return new(world, result.Values.ToArray());
    }

    private static long? Average(JsonElement item, string field, string volume) =>
        item.TryGetProperty(field, out var price) && price.TryGetInt64(out var value) && value > 0
        && item.TryGetProperty(volume, out var count) && count.TryGetInt64(out var transactions) && transactions > 0 ? value : null;
    private static string? String(JsonElement item, string key) => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private string Url(string path) => new Uri(new Uri(options.CurrentValue.BaseUrl), path).ToString();

    private async Task<JsonDocument> ReadAsync(string url, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var response = await SendAsync(url, token);
            if (attempt < 2 && ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500))
            {
                var delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromSeconds(2 << attempt);
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 1, 60)), token);
                continue;
            }
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            return await JsonDocument.ParseAsync(stream, cancellationToken: token);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(string url, CancellationToken token)
    {
        if (new Uri(url).Authority != new Uri(options.CurrentValue.BaseUrl).Authority)
            return await http.GetAsync(url, token);
        await requestGate.WaitAsync(token);
        try
        {
            var wait = nextRequest - clock.GetUtcNow();
            if (wait > TimeSpan.Zero) await Task.Delay(wait, clock, token);
            nextRequest = clock.GetUtcNow().AddSeconds(Math.Max(0, options.CurrentValue.RequestIntervalSeconds));
            return await http.GetAsync(url, token);
        }
        finally { requestGate.Release(); }
    }
}
