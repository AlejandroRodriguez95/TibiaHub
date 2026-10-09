namespace TibiaHub.External.TibiaData;

public sealed class TibiaDataOptions
{
    public const string SectionName = "TibiaData";

    public string BaseUrl { get; init; } = "https://api.tibiadata.com/v4/";

    public string UserAgent { get; init; } = "TibiaHub/0.1";
}
