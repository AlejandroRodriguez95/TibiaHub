using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TibiaIntel.DataRefresh;
using TibiaIntel.DataRefresh.Market;

namespace TibiaIntel.Tests;

public sealed class UpstreamRequestPolicyTests
{
    [Theory]
    [InlineData(0, 6)]
    [InlineData(-1, 6)]
    [InlineData(12, 12)]
    public async Task MarketSpacingHasEnforcedFloor(int configured, int expected)
    {
        var clock = new TestClock();
        var policy = CreatePolicy(clock, configured);
        using var handler = new Source(clock);
        using var http = new HttpClient(handler);
        using var first = await policy.GetAsync(http, "https://market.test/world_data", default);
        var second = policy.GetAsync(http, "https://market.test/market_values", default);
        Assert.Equal(TimeSpan.FromSeconds(expected), await clock.NextDelay());
        clock.Advance(TimeSpan.FromSeconds(expected - 1));
        Assert.Single(handler.Starts);
        clock.Advance(TimeSpan.FromSeconds(1));
        using var result = await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(expected), handler.Starts.Last() - handler.Starts.First());
    }

    [Fact]
    public async Task WorldAndNpcClientsShareHostBudgetButOtherHostsProceed()
    {
        var clock = new TestClock();
        var policy = CreatePolicy(clock);
        using var handler = new Source(clock);
        using var worldHttp = new HttpClient(handler, disposeHandler: false);
        using var npcHttp = new HttpClient(handler, disposeHandler: false);
        using var first = await policy.GetAsync(worldHttp, "https://wiki.test/api/worlds", default);
        var npc = policy.GetAsync(npcHttp, "https://wiki.test/api/npcs/Rashid", default);
        Assert.Equal(TimeSpan.FromSeconds(2.1), await clock.NextDelay());
        using var independent = await policy.GetAsync(npcHttp, "https://another.test/page", default);
        Assert.Equal(2, handler.Starts.Count);
        clock.Advance(TimeSpan.FromSeconds(2.1));
        using var result = await npc.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(3, handler.Starts.Count);
        Assert.Equal(TimeSpan.FromSeconds(2.1), handler.Starts.Last() - handler.Starts.First());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetryAfterOverOneMinuteIsRespectedForBothHeaderFormats(bool httpDate)
    {
        var clock = new TestClock();
        var policy = CreatePolicy(clock);
        using var handler = new Source(clock, call => call == 1 ? Throttled(clock, httpDate) : new(HttpStatusCode.OK));
        using var http = new HttpClient(handler);
        var request = policy.GetAsync(http, "https://wiki.test/api/worlds", default);
        Assert.Equal(TimeSpan.FromSeconds(120), await clock.NextDelay());
        clock.Advance(TimeSpan.FromSeconds(119));
        Assert.Single(handler.Starts);
        clock.Advance(TimeSpan.FromSeconds(1));
        using var result = await request.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(120), handler.Starts.Last() - handler.Starts.First());
    }

    [Fact]
    public async Task FinalFailedAttemptStillBlocksNextCallOnSameHost()
    {
        var clock = new TestClock();
        var policy = CreatePolicy(clock);
        using var handler = new Source(clock, call => call <= 3 ? Throttled(clock) : new(HttpStatusCode.OK));
        using var http = new HttpClient(handler);
        var request = policy.GetAsync(http, "https://wiki.test/api/worlds", default);
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(TimeSpan.FromSeconds(120), await clock.NextDelay());
            clock.Advance(TimeSpan.FromSeconds(120));
        }
        using var failure = await request.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HttpStatusCode.TooManyRequests, failure.StatusCode);
        Assert.Equal(3, handler.Starts.Count);
        var otherClient = policy.GetAsync(http, "https://wiki.test/api/npcs/Rashid", default);
        Assert.Equal(TimeSpan.FromSeconds(120), await clock.NextDelay());
        clock.Advance(TimeSpan.FromSeconds(120));
        using var success = await otherClient.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(4, handler.Starts.Count);
    }

    [Fact]
    public async Task CancellationReleasesGateWithoutErasingSharedCooldown()
    {
        var clock = new TestClock();
        var policy = CreatePolicy(clock);
        using var handler = new Source(clock, call => call == 1 ? Throttled(clock) : new(HttpStatusCode.OK));
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var request = policy.GetAsync(http, "https://wiki.test/api/worlds", cancellation.Token);
        await clock.NextDelay();
        // A caller queued behind the sleeping request must also be cancellable.
        using var queuedCancellation = new CancellationTokenSource();
        var queued = policy.GetAsync(http, "https://wiki.test/queued", queuedCancellation.Token);
        queuedCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        var next = policy.GetAsync(http, "https://wiki.test/api/npcs/Rashid", default);
        Assert.Equal(TimeSpan.FromSeconds(120), await clock.NextDelay());
        clock.Advance(TimeSpan.FromSeconds(120));
        using var result = await next.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, handler.Starts.Count);
    }

    [Fact]
    public async Task ServerErrorsRetryWithBoundedAttemptsAndSharedBackoff()
    {
        var clock = new TestClock();
        var policy = CreatePolicy(clock);
        using var handler = new Source(clock, _ => new(HttpStatusCode.ServiceUnavailable));
        using var http = new HttpClient(handler);
        var request = policy.GetAsync(http, "https://wiki.test/api/worlds", default);
        Assert.Equal(TimeSpan.FromSeconds(2.1), await clock.NextDelay());
        clock.Advance(TimeSpan.FromSeconds(2.1));
        Assert.Equal(TimeSpan.FromSeconds(4), await clock.NextDelay());
        clock.Advance(TimeSpan.FromSeconds(4));
        using var result = await request.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        Assert.Equal(3, handler.Starts.Count);
    }

    private static UpstreamRequestPolicy CreatePolicy(TimeProvider clock, int interval = 6)
    {
        var options = new ServiceCollection().AddOptions().Configure<MarketSourceOptions>(o =>
        {
            o.BaseUrl = "https://market.test/";
            o.RequestIntervalSeconds = interval;
        }).BuildServiceProvider();
        return new(clock, options.GetRequiredService<IOptionsMonitor<MarketSourceOptions>>());
    }

    private static HttpResponseMessage Throttled(TestClock clock, bool date = false)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        if (date)
        {
            // Upstream clock differs by an hour; its requested delay is still 120 seconds.
            response.Headers.Date = clock.GetUtcNow().AddHours(1);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(response.Headers.Date.Value.AddSeconds(120));
        }
        else response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
        return response;
    }

    private sealed class Source(TestClock clock, Func<int, HttpResponseMessage>? respond = null) : HttpMessageHandler
    {
        public ConcurrentQueue<DateTimeOffset> Starts { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Starts.Enqueue(clock.GetUtcNow());
            return Task.FromResult(respond?.Invoke(Starts.Count) ?? new(HttpStatusCode.OK));
        }
    }

    private sealed class TestClock : TimeProvider
    {
        private readonly FakeTimeProvider fake = new(new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero));
        private readonly Channel<TimeSpan> delays = Channel.CreateUnbounded<TimeSpan>();
        public override DateTimeOffset GetUtcNow() => fake.GetUtcNow();
        public void Advance(TimeSpan duration) => fake.Advance(duration);
        public Task<TimeSpan> NextDelay() => delays.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = fake.CreateTimer(callback, state, dueTime, period);
            delays.Writer.TryWrite(dueTime);
            return timer;
        }
    }
}
