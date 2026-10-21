using Microsoft.Extensions.Options;
using TibiaIntel.DataRefresh;

namespace TibiaIntel.CharacterScanning;

// Independent due times keep one unavailable world from delaying the others.
public sealed class OnlineCollector(IServiceScopeFactory scopes, OnlineWorldClient source,
    IOptionsMonitor<CharacterScanOptions> options, IOptionsMonitor<RefreshOptions> refresh,
    TimeProvider clock, ILogger<OnlineCollector> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var due = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        var discoverAt = DateTimeOffset.MinValue;
        var pruneAt = DateTimeOffset.MinValue;
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (options.CurrentValue.Enabled && refresh.CurrentValue.Enabled)
                {
                    if (clock.GetUtcNow() >= discoverAt)
                    {
                        try
                        {
                            var worlds = await source.WorldsAsync(token);
                            foreach (var removed in due.Keys.Except(worlds, StringComparer.OrdinalIgnoreCase).ToArray()) due.Remove(removed);
                            foreach (var world in worlds) due.TryAdd(world, DateTimeOffset.MinValue);
                            discoverAt = clock.GetUtcNow().AddHours(6);
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                        catch (Exception e) { logger.LogWarning(e, "Could not discover scan worlds"); discoverAt = clock.GetUtcNow().AddMinutes(1); }
                    }
                    var ready = due.Where(w => w.Value <= clock.GetUtcNow()).Select(w => w.Key).ToArray();
                    // Each world is attempted once per batch; network concurrency is bounded.
                    await Parallel.ForEachAsync(ready, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token }, async (world, ct) =>
                    {
                        try
                        {
                            var snapshot = await source.FetchAsync(world, ct);
                            await using var scope = scopes.CreateAsyncScope();
                            await scope.ServiceProvider.GetRequiredService<SessionRecorder>().RecordAsync(snapshot, ct);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                        catch (Exception e)
                        {
                            logger.LogWarning(e, "Online collection failed for {World}; retaining last state", world);
                            try
                            {
                                await using var scope = scopes.CreateAsyncScope();
                                await scope.ServiceProvider.GetRequiredService<SessionRecorder>().RecordFailureAsync(world, clock.GetUtcNow(), ct);
                            }
                            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                            catch (Exception failure) { logger.LogError(failure, "Could not record collection gap for {World}", world); }
                        }
                        finally { lock (due) due[world] = clock.GetUtcNow().AddSeconds(Math.Max(60, options.CurrentValue.PollSeconds)); }
                    });
                    if (clock.GetUtcNow() >= pruneAt)
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        await scope.ServiceProvider.GetRequiredService<SessionRecorder>().PruneAsync(clock.GetUtcNow(), token);
                        pruneAt = clock.GetUtcNow().AddDays(1);
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(5), clock, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception e)
            {
                logger.LogError(e, "Character monitoring failed; retrying shortly");
                try { await Task.Delay(TimeSpan.FromSeconds(30), clock, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            }
        }
    }
}
