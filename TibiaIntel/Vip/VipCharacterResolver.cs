using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TibiaIntel.DataRefresh;
using TibiaIntel.External.TibiaData;

namespace TibiaIntel.Vip;

public sealed record VipIdentity(string Name, string World);
public interface IVipCharacterResolver
{
    Task<VipIdentity> ResolveAsync(string name, CancellationToken token);
}

// Only adding a character calls upstream. Presence always comes from the existing collector.
public sealed class VipCharacterResolver(HttpClient http, IOptions<TibiaDataOptions> options,
    UpstreamRequestPolicy policy) : IVipCharacterResolver
{
    public async Task<VipIdentity> ResolveAsync(string name, CancellationToken token)
    {
        try
        {
            var url = new Uri(new Uri(options.Value.BaseUrl), "character/" + Uri.EscapeDataString(name));
            using var response = await policy.GetAsync(http, url.ToString(), token);
            if (response.StatusCode == HttpStatusCode.NotFound) throw new VipException(404, "Character not found.");
            if (!response.IsSuccessStatusCode) throw new VipException(502, "Character lookup is unavailable. Try again later.");
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
            var character = json.RootElement.GetProperty("character").GetProperty("character");
            var display = character.GetProperty("name").GetString();
            var world = character.GetProperty("world").GetString();
            if (string.IsNullOrWhiteSpace(display) || display.Length > 100 || string.IsNullOrWhiteSpace(world)
                || world.Length > 100 || !world.All(char.IsAsciiLetter))
                throw new VipException(502, "Character lookup returned incomplete data.");
            return new(display, world);
        }
        catch (HttpRequestException) { throw new VipException(502, "Character lookup is unavailable. Try again later."); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new VipException(504, "Character lookup timed out. Try again later."); }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new VipException(502, "Character lookup returned incomplete data."); }
    }
}
