namespace TibiaIntel.DataRefresh;

public sealed class RefreshOptions
{
    public bool Enabled { get; set; } = true;
    public string CacheDirectory { get; set; } = "App_Data/refresh";
    public List<RefreshSchedule> Schedules { get; set; } = [];
}

public sealed class RefreshSchedule
{
    public string Job { get; set; } = "";
    public string? DailyAt { get; set; }
    public string TimeZone { get; set; } = "Europe/Vienna";
    public int? IntervalMinutes { get; set; }
    public int RetryMinutes { get; set; } = 15;

    public DateTimeOffset DueAt(DateTimeOffset now, DateTimeOffset? lastSuccess)
    {
        if (RetryMinutes <= 0) throw new InvalidOperationException("RetryMinutes must be positive.");
        if (IntervalMinutes is int minutes)
        {
            if (minutes <= 0 || DailyAt is not null)
                throw new InvalidOperationException("Choose a positive interval OR a daily time.");
            return lastSuccess?.AddMinutes(minutes) ?? now;
        }

        if (!TimeOnly.TryParseExact(DailyAt, "HH:mm", out var time))
            throw new InvalidOperationException("DailyAt must use HH:mm.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        var localDue = DateTime.SpecifyKind(localNow.Date + time.ToTimeSpan(), DateTimeKind.Unspecified);
        if (localNow.DateTime < localDue) localDue = localDue.AddDays(-1);
        // For a DST gap use the next valid minute; for an overlap use the later occurrence.
        while (zone.IsInvalidTime(localDue)) localDue = localDue.AddMinutes(1);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localDue, zone), TimeSpan.Zero);
    }
}

public interface IRefreshJob
{
    string Name { get; }
    Task<DateTimeOffset?> LastSuccessAsync(CancellationToken cancellationToken);
    Task RefreshAsync(CancellationToken cancellationToken);
}
