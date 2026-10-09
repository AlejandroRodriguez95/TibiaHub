using System.Text.Json;

namespace TibiaHub.External.TibiaData;

public sealed record TibiaDataResponse(
    bool IsSuccess,
    int StatusCode,
    JsonElement? Payload,
    string? ErrorMessage)
{
    public static TibiaDataResponse Success(JsonElement payload)
    {
        return new TibiaDataResponse(true, StatusCodes.Status200OK, payload, null);
    }

    public static TibiaDataResponse Failure(int statusCode, string errorMessage)
    {
        return new TibiaDataResponse(false, statusCode, null, errorMessage);
    }
}
