using TibiaIntel.Identity;

namespace TibiaIntel.Vip;

public sealed class VipOptions
{
    public int MaxCharactersPerUser { get; set; } = 1000;
    public int MaxGroupsPerUser { get; set; } = 100;
    public int StatusMaxAgeSeconds { get; set; } = 120;
}

public sealed class VipCharacter
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public string World { get; set; } = "";
    public List<VipMembership> Memberships { get; set; } = [];
}

public sealed class VipGroup
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
    public string Name { get; set; } = "";
    public List<VipMembership> Memberships { get; set; } = [];
}

public sealed class VipMembership
{
    public long CharacterId { get; set; }
    public VipCharacter Character { get; set; } = null!;
    public long GroupId { get; set; }
    public VipGroup Group { get; set; } = null!;
}

public sealed record VipEntry(long Id, string Name, string World, long[] GroupIds, string Status, DateTimeOffset? LastObservedAt);
public sealed record VipGroupResponse(long Id, string Name);
public sealed record VipSnapshot(DateTimeOffset CapturedAt, int MaxCharacters, int MaxGroups, VipEntry[] Characters, VipGroupResponse[] Groups);
public sealed record VipStatus(long Id, string Status, DateTimeOffset? LastObservedAt);
public sealed record VipStatusUpdate(DateTimeOffset CapturedAt, VipStatus[] Characters);
public sealed class VipException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
