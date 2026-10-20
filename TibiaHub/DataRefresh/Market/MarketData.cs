using System.Collections.Concurrent;

namespace TibiaHub.DataRefresh.Market;

public sealed class MarketSourceOptions
{
    public string BaseUrl { get; set; } = "https://api.tibiamarket.top:8001/";
    public string WorldsUrl { get; set; } = "https://tibiawiki.dev/api/worlds";
    // Change the basis without downloading again: both averages are retained.
    public string AverageBasis { get; set; } = "buy";
    public int RequestIntervalSeconds { get; set; } = 6;
    public string SourceKey => "market-v1|" + BaseUrl + "|" + WorldsUrl;
}

public sealed record MarketWorldList(string[] Worlds, DateTimeOffset CycleStartedAt, string[] PendingWorlds);
public sealed record MarketItemMetadata(int Id, string? WikiName, string? Name);
public sealed record MarketItemIndex(IReadOnlyList<MarketItemMetadata> Items);
public sealed record MarketItemPrice(int Id, long? MonthlyBuyAverage, long? MonthlySellAverage, DateTimeOffset SourceUpdatedAt);
public sealed record MarketWorldPrices(string World, IReadOnlyList<MarketItemPrice> Items);

public sealed class MarketWorldStore(IServiceProvider services)
{
    private readonly ConcurrentDictionary<string, JsonSnapshotStore<MarketWorldPrices>> stores = new(StringComparer.OrdinalIgnoreCase);
    public JsonSnapshotStore<MarketWorldPrices> For(string world) => stores.GetOrAdd(world,
        name => ActivatorUtilities.CreateInstance<JsonSnapshotStore<MarketWorldPrices>>(services, name));
}

public static class MarketItemMatcher
{
    // Prefer exact wiki titles. Never guess among ambiguous names or item variants.
    public static Dictionary<string, int> BuildIndex(IEnumerable<MarketItemMetadata> items)
    {
        var array = items.ToArray();
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in array.Where(i => !string.IsNullOrWhiteSpace(i.WikiName)).GroupBy(i => i.WikiName!.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            if (group.Select(i => i.Id).Distinct().Count() == 1) result[group.Key] = group.First().Id;
            else ambiguous.Add(group.Key);
        }
        foreach (var group in array.Where(i => !string.IsNullOrWhiteSpace(i.Name)).GroupBy(i => i.Name!.Trim(), StringComparer.OrdinalIgnoreCase))
            if (group.Select(i => i.Id).Distinct().Count() == 1 && !result.ContainsKey(group.Key) && !ambiguous.Contains(group.Key)) result[group.Key] = group.First().Id;
        return result;
    }
}
