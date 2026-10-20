using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace TibiaHub.DataRefresh.NpcTrades;

// Only the public MediaWiki trade/task tables are accepted, never arbitrary page links.
public static class WikiTradeParser
{
    private static MatchCollection Matches(string input, string pattern) =>
        Regex.Matches(input, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
    private static string Text(string html) => WebUtility.HtmlDecode(
        Regex.Replace(html, "<[^>]*>", "", RegexOptions.Singleline, TimeSpan.FromSeconds(2))).Trim();

    private static string? ItemName(string cell)
    {
        var link = Matches(cell, "<a\\b[^>]*href=\"/wiki/[^\"]+\"[^>]*>(.*?)</a>")
            .FirstOrDefault(m => !m.Groups[1].Value.Contains('<'));
        return link is null ? null : Text(link.Groups[1].Value);
    }

    private static IEnumerable<string[]> Rows(string table) =>
        Matches(table, @"<tr\b[^>]*>(.*?)</tr>")
            .Select(row => Matches(row.Groups[1].Value, @"<td\b[^>]*>(.*?)</td>")
                .Select(cell => cell.Groups[1].Value).ToArray());

    public static IReadOnlyList<NpcPurchaseItem> Purchases(string html, ISet<string> taskItems)
    {
        var section = Matches(html, "id=\"npc-trade-buys\"[^>]*>.*?(<table\\b.*?</table>)").FirstOrDefault()
            ?? throw new InvalidDataException("NPC purchase table was not found; source markup may have changed.");
        var result = new List<NpcPurchaseItem>();
        foreach (var cells in Rows(section.Groups[1].Value))
        {
            if (cells.Length == 0) continue;
            if (cells.Length != 3) throw new InvalidDataException("Unexpected NPC purchase row.");
            var name = ItemName(cells[1]) ?? throw new InvalidDataException("Missing purchase item name.");
            var priceText = Text(cells[2]).Replace(",", "");
            var number = Matches(priceText, @"^\d+").FirstOrDefault()?.Value;
            if (!long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var price) || price <= 0)
                throw new InvalidDataException($"Invalid NPC price for {name}.");
            var image = Matches(cells[0], "(?:data-src|src)=\"(https://static\\.wikia\\.nocookie\\.net/[^\"]+)\"").FirstOrDefault();
            result.Add(new(name, price, image is null ? null : WebUtility.HtmlDecode(image.Groups[1].Value), taskItems.Contains(name)));
        }
        if (result.Count == 0) throw new InvalidDataException("NPC purchase table contained no items.");
        return result.DistinctBy(i => i.Name, StringComparer.OrdinalIgnoreCase).OrderBy(i => i.Name).ToArray();
    }

    public static HashSet<string> DeliveryItems(string html)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match table in Matches(html, @"<table\b[^>]*>(.*?)</table>"))
        {
            var content = table.Groups[1].Value;
            var headers = Matches(content, @"<th\b[^>]*>(.*?)</th>").Select(m => Text(m.Groups[1].Value)).ToArray();
            if (!headers.Contains("Name") || !headers.Any(h => h.Contains("Min", StringComparison.OrdinalIgnoreCase))) continue;
            foreach (var cells in Rows(content))
            {
                if (cells.Length == 0) continue;
                if (cells.Length < 4 || ItemName(cells[1]) is not string name)
                    throw new InvalidDataException("Unexpected delivery-task item row.");
                result.Add(name);
            }
        }
        if (result.Count == 0) throw new InvalidDataException("Delivery-task tables contained no items.");
        return result;
    }
}
