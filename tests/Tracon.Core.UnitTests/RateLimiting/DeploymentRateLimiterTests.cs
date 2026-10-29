using Microsoft.Extensions.Logging;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.RateLimiting;

/// <summary>
/// Verifies <see cref="DeploymentRateLimiter"/>: the count against the shared store, and what happens when the
/// store fails.
/// </summary>
public sealed class DeploymentRateLimiterTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private static readonly RateLimitPartitionKey Key = RateLimitPartitionKey.ForTenant(RateLimitGroups.Management, "acme");

    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task Requests_within_the_limit_are_allowed_and_the_next_is_rejected_with_the_wait()
    {
        var clock = new ManualTimeProvider(Start.AddSeconds(15));
        var limiter = new DeploymentRateLimiter(new InMemoryRateLimitCounterStore(), clock);

        for (var count = 0; count < 3; count++)
        {
            (await Count(limiter)).ShouldNotBeNull().Allowed.ShouldBeTrue();
        }

        var rejected = (await Count(limiter)).ShouldNotBeNull();

        rejected.Allowed.ShouldBeFalse();
        rejected.RetryAfter.ShouldBe(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public async Task The_window_ends_for_every_instance_at_the_same_moment()
    {
        // Two limiters share one store: their requests are one stream, and a new window opens for both at once.
        var clock = new ManualTimeProvider(Start.AddSeconds(15));
        var store = new InMemoryRateLimitCounterStore();
        var first = new DeploymentRateLimiter(store, clock);
        var second = new DeploymentRateLimiter(store, clock);

        (await Count(first, limit: 2)).ShouldNotBeNull().Allowed.ShouldBeTrue();
        (await Count(second, limit: 2)).ShouldNotBeNull().Allowed.ShouldBeTrue();
        (await Count(first, limit: 2)).ShouldNotBeNull().Allowed.ShouldBeFalse();
        (await Count(second, limit: 2)).ShouldNotBeNull().Allowed.ShouldBeFalse();

        clock.Advance(TimeSpan.FromSeconds(46));

        (await Count(second, limit: 2)).ShouldNotBeNull().Allowed.ShouldBeTrue();
        (await Count(first, limit: 2)).ShouldNotBeNull().Allowed.ShouldBeTrue();
        (await Count(first, limit: 2)).ShouldNotBeNull().Allowed.ShouldBeFalse();
    }

    [Fact]
    public async Task A_store_failure_gives_no_decision_and_is_counted_by_limiter_kind()
    {
        using var meters = new TestMeterFactory();
        using var collector = new MetricCollector(meters.Meter);
        using var metrics = new TraconMetrics(meters);
        var limiter = new DeploymentRateLimiter(new FailingStore(), new ManualTimeProvider(Start), metrics);

        (await Count(limiter, kind: RateLimiterKind.Endpoint)).ShouldBeNull();
        (await Count(limiter, kind: RateLimiterKind.Trigger)).ShouldBeNull();

        collector.LongValues(TraconDiagnostics.RateLimitStoreFailureCounterName).ShouldBe([1L, 1L]);
        collector.LongTags(TraconDiagnostics.RateLimitStoreFailureCounterName)
            .Select(static tags => tags[TraconDiagnostics.Tags.Limiter])
            .ShouldBe([RateLimiterKind.Endpoint, RateLimiterKind.Trigger]);
    }

    [Fact]
    public async Task A_store_failure_is_logged_once_a_minute_for_each_partition()
    {
        var clock = new ManualTimeProvider(Start);
        var logger = new SpyLogger();
        var limiter = new DeploymentRateLimiter(new FailingStore(), clock, logger: logger);
        var other = RateLimitPartitionKey.ForTenant(RateLimitGroups.Management, "globex");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Count(limiter);
        }

        logger.Warnings.Count.ShouldBe(1);

        // Another partition has its own clock for the warning.
        await limiter.TryCountAsync(other, 3, Minute, RateLimiterKind.Endpoint, CancellationToken.None);

        logger.Warnings.Count.ShouldBe(2);

        clock.Advance(DeploymentRateLimiter.WarningInterval + TimeSpan.FromSeconds(1));
        await Count(limiter);

        logger.Warnings.Count.ShouldBe(3);
        logger.Warnings[0].ShouldContain("endpoint:tracon:tenant:acme");
    }

    [Fact]
    public async Task A_cancelled_caller_is_not_a_store_failure()
    {
        using var meters = new TestMeterFactory();
        using var collector = new MetricCollector(meters.Meter);
        using var metrics = new TraconMetrics(meters);
        var limiter = new DeploymentRateLimiter(new CancellationAwareStore(), new ManualTimeProvider(Start), metrics);
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await limiter.TryCountAsync(Key, 3, Minute, RateLimiterKind.Endpoint, source.Token));

        collector.LongValues(TraconDiagnostics.RateLimitStoreFailureCounterName).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_cancellation_the_store_raised_on_its_own_is_a_store_failure()
    {
        // A timeout inside the store surfaces as OperationCanceledException while the CALLER's token is live.
        using var meters = new TestMeterFactory();
        using var collector = new MetricCollector(meters.Meter);
        using var metrics = new TraconMetrics(meters);
        var limiter = new DeploymentRateLimiter(new FailingStore(new OperationCanceledException("store timeout")), new ManualTimeProvider(Start), metrics);

        (await Count(limiter)).ShouldBeNull();

        collector.LongValues(TraconDiagnostics.RateLimitStoreFailureCounterName).ShouldBe([1L]);
    }

    [Fact]
    public async Task The_store_receives_the_key_text_and_the_aligned_window_start()
    {
        var store = new RecordingStore();
        var limiter = new DeploymentRateLimiter(store, new ManualTimeProvider(Start.AddSeconds(33)));

        await Count(limiter);

        store.Calls.ShouldBe([("endpoint:tracon:tenant:acme", Start)]);
    }

    [Fact]
    public void The_warning_about_a_counter_only_this_process_can_see_is_written_once()
    {
        var logger = new SpyLogger();
        var limiter = new DeploymentRateLimiter(new InMemoryRateLimitCounterStore(), logger: logger);

        limiter.StoreIsProcessLocal.ShouldBeTrue();

        limiter.WarnWhenProcessLocal();
        limiter.WarnWhenProcessLocal();

        logger.Warnings.Count.ShouldBe(1);
        logger.Warnings[0].ShouldContain("Deployment");
    }

    [Fact]
    public void A_shared_store_gives_no_warning()
    {
        var logger = new SpyLogger();
        var limiter = new DeploymentRateLimiter(new RecordingStore(), logger: logger);

        limiter.StoreIsProcessLocal.ShouldBeFalse();

        limiter.WarnWhenProcessLocal();

        logger.Warnings.ShouldBeEmpty();
    }

    private static ValueTask<FixedWindowDecision?> Count(
        DeploymentRateLimiter limiter,
        int limit = 3,
        string kind = RateLimiterKind.Endpoint)
        => limiter.TryCountAsync(Key, limit, Minute, kind, CancellationToken.None);

    private sealed class FailingStore(Exception? failure = null) : IRateLimitCounterStore
    {
        public ValueTask<RateLimitWindowCount> IncrementAsync(
            string partitionKey,
            DateTimeOffset windowStart,
            CancellationToken cancellationToken = default)
            => throw (failure ?? new InvalidOperationException("The database is down."));
    }

    private sealed class CancellationAwareStore : IRateLimitCounterStore
    {
        public ValueTask<RateLimitWindowCount> IncrementAsync(
            string partitionKey,
            DateTimeOffset windowStart,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return new ValueTask<RateLimitWindowCount>(new RateLimitWindowCount { WindowStart = windowStart, Count = 1 });
        }
    }

    private sealed class RecordingStore : IRateLimitCounterStore
    {
        public List<(string Key, DateTimeOffset WindowStart)> Calls { get; } = [];

        public ValueTask<RateLimitWindowCount> IncrementAsync(
            string partitionKey,
            DateTimeOffset windowStart,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((partitionKey, windowStart));

            return new ValueTask<RateLimitWindowCount>(new RateLimitWindowCount { WindowStart = windowStart, Count = Calls.Count });
        }
    }

    private sealed class SpyLogger : ILogger<DeploymentRateLimiter>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
