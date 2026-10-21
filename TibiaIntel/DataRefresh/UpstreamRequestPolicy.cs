using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Options;
using TibiaIntel.DataRefresh.Market;

namespace TibiaIntel.DataRefresh;

// Singleton: all refresh clients share spacing and server-requested cooldowns per host.
// This coordinates one process; deployments must run only one refresh worker per public IP.
public sealed class UpstreamRequestPolicy(TimeProvider clock, IOptionsMonitor<MarketSourceOptions> marketOptions)
{
    private sealed class HostState
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public DateTimeOffset NextRequest = DateTimeOffset.MinValue;
    }

    private readonly ConcurrentDictionary<string, HostState> hosts = new(StringComparer.OrdinalIgnoreCase);

    public async Task<HttpResponseMessage> GetAsync(HttpClient client, string url, CancellationToken token)
    {
        var uri = new Uri(url);
        var state = hosts.GetOrAdd(uri.IdnHost, _ => new HostState());
        for (var attempt = 0; ; attempt++)
        {
            await state.Gate.WaitAsync(token);
            try
            {
                // Queueing and cooldown waits are outside HttpClient's per-request timeout.
                while (state.NextRequest > clock.GetUtcNow())
                {
                    var remaining = state.NextRequest - clock.GetUtcNow();
                    if (remaining <= TimeSpan.Zero) break;
                    await Task.Delay(remaining > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : remaining, clock, token);
                }
                token.ThrowIfCancellationRequested();
                var interval = uri.IdnHost.Equals(new Uri(marketOptions.CurrentValue.BaseUrl).IdnHost, StringComparison.OrdinalIgnoreCase)
                    ? TimeSpan.FromSeconds(Math.Max(6, marketOptions.CurrentValue.RequestIntervalSeconds))
                    // Production TibiaData publishes no requests/second quota. Stagger its
                    // all-world observations; wiki hosts retain their stricter shared budget.
                    : uri.IdnHost.Equals("api.tibiadata.com", StringComparison.OrdinalIgnoreCase)
                        ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromSeconds(2.1);
                state.NextRequest = clock.GetUtcNow() + interval;
                var response = await client.GetAsync(uri, token);
                var retryable = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode is >= 500 and <= 599;
                var retryAfter = response.Headers.RetryAfter;
                if (retryable || retryAfter is not null)
                {
                    // Use the server's Date for HTTP-date delays to tolerate local clock skew.
                    var delay = retryAfter?.Delta
                        ?? (retryAfter?.Date - (response.Headers.Date ?? clock.GetUtcNow()))
                        ?? TimeSpan.FromSeconds(2 << attempt);
                    if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
                    var cooldown = clock.GetUtcNow() + delay;
                    if (cooldown > state.NextRequest) state.NextRequest = cooldown;
                }
                // Record cooldown even on the final failure, so the next job cannot bypass it.
                if (!retryable || attempt >= 2) return response;
                response.Dispose();
            }
            finally { state.Gate.Release(); }
        }
    }
}
