using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.RateLimiting;

/// <summary>
/// Verifies <see cref="InboundTriggerRateLimiter"/> under both scopes: process memory (the default) and the shared
/// counter store.
/// </summary>
public sealed class InboundTriggerRateLimiterTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Trigger = new("0b8f5d3c-7a41-4c1e-9d25-3f6a8e1b2c47");

    private static readonly Guid OtherTrigger = new("5d0a2e61-18bc-4f7a-8a33-9b4c7d6e0f12");

    [Fact]
    public async Task The_instance_scope_counts_in_process_memory_and_never_calls_the_store()
    {
        var store = new CountingStore();
        var clock = new ManualTimeProvider(Start);
        var one = Create(store, clock, RateLimitScope.Instance, limit: 2);
        var two = Create(store, clock, RateLimitScope.Instance, limit: 2);

        // Each limiter has its own window: two instances admit the limit twice.
        (await one.TryAcquireAsync(Trigger)).ShouldBeTrue();
        (await one.TryAcquireAsync(Trigger)).ShouldBeTrue();
        (await one.TryAcquireAsync(Trigger)).ShouldBeFalse();
        (await two.TryAcquireAsync(Trigger)).ShouldBeTrue();

        store.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task The_deployment_scope_shares_one_window_between_instances()
    {
        var store = new InMemoryRateLimitCounterStore();
        var clock = new ManualTimeProvider(Start);
        var one = Create(store, clock, RateLimitScope.Deployment, limit: 2);
        var two = Create(store, clock, RateLimitScope.Deployment, limit: 2);

        (await one.TryAcquireAsync(Trigger)).ShouldBeTrue();
        (await two.TryAcquireAsync(Trigger)).ShouldBeTrue();
        (await one.TryAcquireAsync(Trigger)).ShouldBeFalse();
        (await two.TryAcquireAsync(Trigger)).ShouldBeFalse();
    }

    [Fact]
    public async Task The_deployment_scope_keeps_a_window_for_each_trigger_and_opens_a_new_one_each_minute()
    {
        var store = new InMemoryRateLimitCounterStore();
        var clock = new ManualTimeProvider(Start.AddSeconds(5));
        var limiter = Create(store, clock, RateLimitScope.Deployment, limit: 1);

        (await limiter.TryAcquireAsync(Trigger)).ShouldBeTrue();
        (await limiter.TryAcquireAsync(Trigger)).ShouldBeFalse();
        (await limiter.TryAcquireAsync(OtherTrigger)).ShouldBeTrue();

        clock.Advance(TimeSpan.FromSeconds(56));

        (await limiter.TryAcquireAsync(Trigger)).ShouldBeTrue();
    }

    [Fact]
    public async Task The_deployment_scope_counts_the_trigger_under_its_own_key()
    {
        var store = new CountingStore();
        var limiter = Create(store, new ManualTimeProvider(Start), RateLimitScope.Deployment, limit: 5);

        await limiter.TryAcquireAsync(Trigger);

        store.Keys.ShouldBe(["trigger:0b8f5d3c-7a41-4c1e-9d25-3f6a8e1b2c47"]);
    }

    [Fact]
    public async Task A_store_failure_falls_back_to_the_local_window_and_never_lifts_the_limit()
    {
        var clock = new ManualTimeProvider(Start);
        var limiter = Create(new CountingStore(failing: true), clock, RateLimitScope.Deployment, limit: 2);

        (await limiter.TryAcquireAsync(Trigger)).ShouldBeTrue();
        (await limiter.TryAcquireAsync(Trigger)).ShouldBeTrue();

        // Not fail-open: the limit still holds, per instance.
        (await limiter.TryAcquireAsync(Trigger)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_store_failure_does_not_reject_every_request()
    {
        var limiter = Create(new CountingStore(failing: true), new ManualTimeProvider(Start), RateLimitScope.Deployment, limit: 60);

        // Not fail-closed either.
        (await limiter.TryAcquireAsync(Trigger)).ShouldBeTrue();
    }

    [Fact]
    public async Task The_scope_is_read_on_every_call()
    {
        var store = new CountingStore();
        var scope = new MutableRateLimitOptions();
        var limiter = new InboundTriggerRateLimiter(
            new StaticOptionsMonitor<TraconInboundTriggerOptions>(new TraconInboundTriggerOptions { MaxRequestsPerMinute = 3 }),
            new ManualTimeProvider(Start),
            scope.Monitor,
            new DeploymentRateLimiter(store, new ManualTimeProvider(Start)));

        await limiter.TryAcquireAsync(Trigger);
        store.Calls.ShouldBe(0);

        scope.Options.Scope = RateLimitScope.Deployment;
        await limiter.TryAcquireAsync(Trigger);

        store.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Without_a_rate_limit_configuration_the_window_is_local()
    {
        var limiter = new InboundTriggerRateLimiter(
            new StaticOptionsMonitor<TraconInboundTriggerOptions>(new TraconInboundTriggerOptions { MaxRequestsPerMinute = 1 }),
            new ManualTimeProvider(Start));

        (await limiter.TryAcquireAsync(Trigger)).ShouldBeTrue();
        (await limiter.TryAcquireAsync(Trigger)).ShouldBeFalse();
    }

    private static InboundTriggerRateLimiter Create(
        IRateLimitCounterStore store,
        ManualTimeProvider clock,
        RateLimitScope scope,
        int limit)
        => new(
            new StaticOptionsMonitor<TraconInboundTriggerOptions>(new TraconInboundTriggerOptions { MaxRequestsPerMinute = limit }),
            clock,
            new StaticOptionsMonitor<TraconRateLimitOptions>(new TraconRateLimitOptions { Scope = scope }),
            new DeploymentRateLimiter(store, clock));

    private sealed class MutableRateLimitOptions
    {
        public TraconRateLimitOptions Options { get; } = new();

        public StaticOptionsMonitor<TraconRateLimitOptions> Monitor => new(Options);
    }

    private sealed class CountingStore(bool failing = false) : IRateLimitCounterStore
    {
        private readonly InMemoryRateLimitCounterStore _inner = new();

        public int Calls { get; private set; }

        public List<string> Keys { get; } = [];

        public ValueTask<RateLimitWindowCount> IncrementAsync(
            string partitionKey,
            DateTimeOffset windowStart,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Keys.Add(partitionKey);

            return failing
                ? throw new InvalidOperationException("The database is down.")
                : _inner.IncrementAsync(partitionKey, windowStart, cancellationToken);
        }
    }
}
