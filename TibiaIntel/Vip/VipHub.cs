using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace TibiaIntel.Vip;

// Only connection IDs and authenticated user IDs are retained, never caller-supplied subscriptions.
public sealed class VipConnections
{
    private readonly ConcurrentDictionary<string, string> connections = new();
    public void Add(string connection, string user) => connections[connection] = user;
    public void Remove(string connection) => connections.TryRemove(connection, out _);
    public string[] Users => connections.Values.Distinct().ToArray();
}

[Authorize]
public sealed class VipHub(VipConnections connections, VipService vip) : Hub
{
    public override async Task OnConnectedAsync()
    {
        connections.Add(Context.ConnectionId, Context.UserIdentifier!);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Remove(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    // Read-only. Writes stay on CSRF-protected controller endpoints.
    public Task<VipSnapshot> Snapshot() => vip.ReadAsync(Context.UserIdentifier!, Context.ConnectionAborted);
}

public sealed class VipBroadcaster(IServiceScopeFactory scopes, VipConnections connections,
    IHubContext<VipHub> hub, TimeProvider clock, ILogger<VipBroadcaster> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var previous = new Dictionary<string, VipSnapshot>();
        while (!token.IsCancellationRequested)
        {
            try
            {
                var users = connections.Users;
                foreach (var removed in previous.Keys.Except(users).ToArray()) previous.Remove(removed);
                foreach (var user in users)
                {
                    try
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        var next = await scope.ServiceProvider.GetRequiredService<VipService>().ReadAsync(user, token);
                        previous.TryGetValue(user, out var old);
                        if (old is null || !SameList(old, next))
                            await hub.Clients.User(user).SendAsync("Snapshot", next, token);
                        else
                        {
                            var oldById = old.Characters.ToDictionary(c => c.Id);
                            var changes = next.Characters.Where(c => c.Status != oldById[c.Id].Status)
                                .Select(c => new VipStatus(c.Id, c.Status, c.LastObservedAt)).ToArray();
                            if (changes.Length > 0)
                                await hub.Clients.User(user).SendAsync("Statuses", new VipStatusUpdate(next.CapturedAt, changes), token);
                        }
                        previous[user] = next;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception e)
                    {
                        logger.LogWarning(e, "Could not refresh VIP connection");
                        if (previous.TryGetValue(user, out var stale))
                        {
                            var unknown = stale.Characters.Where(c => c.Status != "unknown")
                                .Select(c => new VipStatus(c.Id, "unknown", c.LastObservedAt)).ToArray();
                            if (unknown.Length > 0)
                            {
                                try
                                {
                                    await hub.Clients.User(user).SendAsync("Statuses", new VipStatusUpdate(clock.GetUtcNow(), unknown), token);
                                    previous[user] = stale with { Characters = stale.Characters.Select(c => c with { Status = "unknown" }).ToArray() };
                                }
                                catch (Exception sendError) when (!token.IsCancellationRequested)
                                { logger.LogWarning(sendError, "Could not notify VIP connection of unavailable data"); }
                            }
                        }
                    }
                }
                // Database reads only, and only for connected accounts. Also detects stale observations and edits in other tabs.
                await Task.Delay(TimeSpan.FromSeconds(5), clock, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
        }
    }

    public static bool SameList(VipSnapshot left, VipSnapshot right) =>
        left.MaxCharacters == right.MaxCharacters && left.MaxGroups == right.MaxGroups
        && left.Groups.SequenceEqual(right.Groups) && left.Characters.Length == right.Characters.Length
        && left.Characters.Zip(right.Characters).All(pair => pair.First.Id == pair.Second.Id
            && pair.First.Name == pair.Second.Name && pair.First.World == pair.Second.World
            && pair.First.GroupIds.SequenceEqual(pair.Second.GroupIds));
}
