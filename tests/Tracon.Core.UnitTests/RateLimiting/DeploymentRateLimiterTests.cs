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
        var clock = new ManualClock(Start.AddSeconds(15));
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
        var clock = new ManualClock(Start.AddSeconds(15));
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
        var limiter = new DeploymentRateLimiter(new FailingStore(), new ManualClock(Start), metrics);

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
        var clock = new ManualClock(Start);
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
        var limiter = new DeploymentRateLimiter(new CancellationAwareStore(), new ManualClock(Start), metrics);
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
        var limiter = new DeploymentRateLimiter(new FailingStore(new OperationCanceledException("store timeout")), new ManualClock(Start), metrics);

        (await Count(limiter)).ShouldBeNull();

        collector.LongValues(TraconDiagnostics.RateLimitStoreFailureCounterName).ShouldBe([1L]);
    }

    [Fact]
    public async Task The_store_receives_the_key_text_and_the_aligned_window_start()
    {
        var store = new RecordingStore();
        var limiter = new DeploymentRateLimiter(store, new ManualClock(Start.AddSeconds(33)));

        await Count(limiter);

        store.Calls.ShouldBe([("endpoint:tracon:tenant:acme", Start)]);
    }

    [Fact]
    public async Task A_stored_window_more_than_one_window_ahead_of_this_clock_is_warned_about_once_a_minute()
    {
        // A clock that jumped forward on another instance moved the stored window ahead; it never moves back, so the
        // partition stays spent until real time catches up. The request is still decided from the stored window.
        var clock = new ManualClock(Start.AddSeconds(15));
        var logger = new SpyLogger();
        var store = new FixedAnswerStore(Start.AddMinutes(10), count: 7);
        var limiter = new DeploymentRateLimiter(store, clock, logger: logger);

        var decision = (await Count(limiter)).ShouldNotBeNull();

        decision.Allowed.ShouldBeFalse();
        decision.RetryAfter.ShouldBe(TimeSpan.FromSeconds(645));

        await Count(limiter);
        await Count(limiter);

        logger.Warnings.Count.ShouldBe(1);
        logger.Warnings[0].ShouldContain("synchronised");
        logger.Warnings[0].ShouldContain("endpoint:tracon:tenant:acme");

        clock.Advance(DeploymentRateLimiter.WarningInterval + TimeSpan.FromSeconds(1));
        await Count(limiter);

        logger.Warnings.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_stored_window_exactly_one_window_ahead_is_the_ordinary_boundary_and_is_not_warned_about()
    {
        var clock = new ManualClock(Start.AddSeconds(59));
        var logger = new SpyLogger();
        var limiter = new DeploymentRateLimiter(new FixedAnswerStore(Start.AddMinutes(1), count: 1), clock, logger: logger);

        await Count(limiter);

        logger.Warnings.ShouldBeEmpty();
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

    [Fact]
    public async Task The_store_is_asked_to_remove_the_windows_that_are_over_once_per_longest_window_and_not_on_every_request()
    {
        var clock = new ManualClock(Start.AddSeconds(15));
        var store = new SweepingStore();
        var limiter = new DeploymentRateLimiter(store, clock);

        // The first request is due at once; the ones that follow inside the minute are not.
        await Count(limiter);
        await Count(limiter);
        clock.Advance(TimeSpan.FromSeconds(30));
        await Count(limiter);

        store.Removals.Count.ShouldBe(1, "a request inside the interval asked for another removal");
        store.Removals[0].ShouldBe(Start.AddSeconds(15) - Minute, "the cutoff is the time minus the longest window");

        // 12:01:15 and later: due again, with the cutoff of that moment.
        clock.Advance(TimeSpan.FromSeconds(31));
        await Count(limiter);

        store.Removals.Count.ShouldBe(2);
        store.Removals[1].ShouldBe(Start.AddSeconds(76) - Minute);
    }

    [Fact]
    public async Task The_longest_window_the_limiter_has_seen_sets_the_cutoff()
    {
        var clock = new ManualClock(Start.AddSeconds(15));
        var store = new SweepingStore();
        var limiter = new DeploymentRateLimiter(store, clock);
        var hour = TimeSpan.FromHours(1);

        await limiter.TryCountAsync(Key, 3, Minute, RateLimiterKind.Endpoint, CancellationToken.None);
        await limiter.TryCountAsync(Key, 3, hour, RateLimiterKind.Endpoint, CancellationToken.None);

        // The next removal is an hour away, and a minute window must not make it earlier than the hour allows.
        clock.Advance(TimeSpan.FromMinutes(61));
        await limiter.TryCountAsync(Key, 3, Minute, RateLimiterKind.Endpoint, CancellationToken.None);

        store.Removals.Count.ShouldBe(2);
        store.Removals[1].ShouldBe(Start.AddSeconds(15).AddMinutes(61) - hour, "a partition with an hour window would lose its counter");
    }

    [Fact]
    public async Task A_store_that_fails_to_remove_the_windows_does_not_fail_the_request_and_is_logged()
    {
        var logger = new SpyLogger();
        var store = new SweepingStore { Failure = new InvalidOperationException("The database is down.") };
        var limiter = new DeploymentRateLimiter(store, new ManualClock(Start.AddSeconds(15)), logger: logger);

        var decision = (await Count(limiter)).ShouldNotBeNull();

        decision.Allowed.ShouldBeTrue("the request was counted before the removal was asked");
        store.Removals.Count.ShouldBe(1);
        logger.Warnings.ShouldHaveSingleItem().ShouldContain("could not remove the windows");
    }

    [Fact]
    public async Task A_cancelled_request_does_not_ask_the_store_to_remove_anything()
    {
        var store = new SweepingStore();
        var limiter = new DeploymentRateLimiter(store, new ManualClock(Start.AddSeconds(15)));
        using var cancelled = new CancellationTokenSource();

        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await limiter.TryCountAsync(Key, 3, Minute, RateLimiterKind.Endpoint, cancelled.Token));

        store.Removals.ShouldBeEmpty();
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

    /// <summary>A store that counts like a window store does and records every removal it is asked for.</summary>
    private sealed class SweepingStore : IRateLimitCounterStore
    {
        private readonly InMemoryRateLimitCounterStore _inner = new();

        public List<DateTimeOffset> Removals { get; } = [];

        public Exception? Failure { get; init; }

        public ValueTask<RateLimitWindowCount> IncrementAsync(
            string partitionKey,
            DateTimeOffset windowStart,
            CancellationToken cancellationToken = default)
            => _inner.IncrementAsync(partitionKey, windowStart, cancellationToken);

        public ValueTask<int> RemoveEndedWindowsAsync(DateTimeOffset startedBefore, CancellationToken cancellationToken = default)
        {
            Removals.Add(startedBefore);

            return Failure is { } failure ? throw failure : _inner.RemoveEndedWindowsAsync(startedBefore, cancellationToken);
        }
    }

    private sealed class FixedAnswerStore(DateTimeOffset windowStart, long count) : IRateLimitCounterStore
    {
        public ValueTask<RateLimitWindowCount> IncrementAsync(
            string partitionKey,
            DateTimeOffset requestedWindowStart,
            CancellationToken cancellationToken = default)
            => new(new RateLimitWindowCount { WindowStart = windowStart, Count = count });
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
