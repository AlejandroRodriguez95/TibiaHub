using System.Collections.Concurrent;

namespace TibiaHub.DataRefresh.Market;

// Aggregate popularity only: no user identifiers or per-request disk writes.
// Counts last for this process and are updated only for validated world selections.
public sealed class WorldRequestTracker
{
    private readonly ConcurrentDictionary<string, long> counts = new(StringComparer.OrdinalIgnoreCase);
    public void Record(string world) => counts.AddOrUpdate(world, 1, (_, count) => count == long.MaxValue ? count : count + 1);
    public long Count(string world) => counts.GetValueOrDefault(world);
}
