namespace TibiaIntel.CharacterScanning;

public sealed class CharacterScanOptions
{
    public bool Enabled { get; set; } = true;
    public int PollSeconds { get; set; } = 60;
    public int MaximumGapSeconds { get; set; } = 90;
    public int MassChangeMinimum { get; set; } = 20;
    public double MassChangeFraction { get; set; } = 0.4;
    public string ServerSaveTimeZone { get; set; } = "Europe/Berlin";
    public string ServerSaveStart { get; set; } = "09:55";
    public string ServerSaveEnd { get; set; } = "10:15";
}

public sealed class ScannedCharacter
{
    public long Id { get; set; }
    public string World { get; set; } = "";
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
}

public sealed class OnlineSession
{
    public long Id { get; set; }
    public long CharacterId { get; set; }
    public ScannedCharacter Character { get; set; } = null!;
    public DateTimeOffset LoginAt { get; set; }
    public DateTimeOffset? LoginAfter { get; set; }
    public bool LoginReliable { get; set; }
    public DateTimeOffset? LogoutAt { get; set; }
    public DateTimeOffset? LogoutAfter { get; set; }
    public bool LogoutReliable { get; set; }
}

public sealed class WorldObservation
{
    public long Id { get; set; }
    public string World { get; set; } = "";
    public string SourceKey { get; set; } = "";
    public DateTimeOffset? SourceAt { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
    public int OnlineCount { get; set; }
    // Empty means a reliable transition; other values explain exclusions.
    public string Exclusion { get; set; } = "";
}

public sealed record OnlineWorldSnapshot(string World, string SourceKey, DateTimeOffset SourceAt,
    DateTimeOffset FetchedAt, bool Available, string[] Names);
public sealed record ScanCandidate(string Name, int Score, int FirstMinuteMatches, int SecondMinuteMatches);
public sealed record CharacterScanResult(string World, string Character, DateTimeOffset WindowStart,
    DateTimeOffset? HistoryStart, DateTimeOffset? LastObservation, int ObservedLogouts, int UsableLogouts,
    IReadOnlyList<ScanCandidate> Candidates);
