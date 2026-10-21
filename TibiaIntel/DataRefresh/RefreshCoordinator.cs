using Microsoft.Extensions.Options;

namespace TibiaIntel.DataRefresh;

// One worker per job: slow sources never block unrelated refresh jobs.
public sealed class RefreshCoordinator(
    IEnumerable<IRefreshJob> jobs,
    IOptionsMonitor<RefreshOptions> options,
    TimeProvider clock,
    ILogger<RefreshCoordinator> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(jobs.Select(job => RunJobAsync(job, stoppingToken)));

    private async Task RunJobAsync(IRefreshJob job, CancellationToken token)
    {
        DateTimeOffset retryAt = DateTimeOffset.MinValue;
        while (!token.IsCancellationRequested)
        {
            RefreshSchedule? schedule = null;
            try
            {
                schedule = options.CurrentValue.Schedules.SingleOrDefault(s => s.Job == job.Name);
                if (options.CurrentValue.Enabled && schedule is not null && clock.GetUtcNow() >= retryAt)
                {
                    var last = await job.LastSuccessAsync(token);
                    var now = clock.GetUtcNow();
                    var due = schedule.DueAt(now, last);
                    if (last is null || (schedule.IntervalMinutes is not null ? now >= due : last < due))
                    {
                        logger.LogInformation("Refreshing {Job}", job.Name);
                        await job.RefreshAsync(token);
                        logger.LogInformation("Refresh completed: {Job}", job.Name);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Refresh failed: {Job}; retaining previous snapshot", job.Name);
                retryAt = clock.GetUtcNow().AddMinutes(Math.Max(1, schedule?.RetryMinutes ?? 15));
            }

            try { await Task.Delay(TimeSpan.FromSeconds(30), clock, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
        }
    }
}
