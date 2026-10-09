using System.Net;
using System.Text.Json;

namespace TibiaHub.External.TibiaData;

public sealed class TibiaDataClient(HttpClient httpClient)
{
    public async Task<TibiaDataResponse> GetCharacterAsync(
        string characterName,
        CancellationToken cancellationToken)
    {
        var requestUri = $"character/{Uri.EscapeDataString(characterName.Trim())}";
        using var response = await httpClient.GetAsync(requestUri, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return TibiaDataResponse.Failure(
                (int)response.StatusCode,
                $"Upstream TibiaData returned {(int)response.StatusCode} ({response.StatusCode}).");
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return TibiaDataResponse.Failure(
                (int)HttpStatusCode.BadGateway,
                "Upstream TibiaData returned an empty response.");
        }

        try
        {
            var payload = JsonSerializer.Deserialize<JsonElement>(responseBody);
            return TibiaDataResponse.Success(payload);
        }
        catch (JsonException)
        {
            return TibiaDataResponse.Failure(
                (int)HttpStatusCode.BadGateway,
                "Upstream TibiaData returned invalid JSON.");
        }
    }
}
