namespace TibiaHub.DataRefresh.NpcTrades;

public sealed class NpcTradeSourceOptions
{
    public string[] NpcNames { get; set; } = [];
    public string WikiApiUrl { get; set; } = "https://tibia.fandom.com/api.php";
    public string TibiaWikiApiUrl { get; set; } = "https://tibiawiki.dev/";

    public string[] Names => NpcNames.Select(n => n.Trim()).Where(n => n.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    public string SourceKey => string.Join("|", Names).ToUpperInvariant() + "|" + WikiApiUrl + "|" + TibiaWikiApiUrl;
}

public sealed record NpcTradeCatalog(IReadOnlyList<NpcPurchases> Npcs);
public sealed record NpcPurchases(string Name, IReadOnlyList<NpcPurchaseItem> Items);
public sealed record NpcPurchaseItem(string Name, long NpcPrice, string? IconUrl, bool IsDeliveryTask);
