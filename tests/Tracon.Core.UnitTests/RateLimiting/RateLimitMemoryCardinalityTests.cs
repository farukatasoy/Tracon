using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.RateLimiting;

/// <summary>
/// What the deployment rate limiter keeps in memory follows the partitions that are active, not the partitions
/// that ever were.
/// </summary>
/// <remarks>
/// <para>
/// The partition key carries the tenant, and an installation that accepts any tenant identifier lets a caller make
/// up partitions. The in-memory counter kept one entry for every partition for the life of the process, and the two
/// warning maps kept one for every partition that ever failed or skewed.
/// </para>
/// <para>
/// A removal must not change what a partition is allowed: an entry is removed only when its window is over, and the
/// count of a window that is still open keeps counting.
/// </para>
/// </remarks>
public sealed class RateLimitMemoryCardinalityTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    private static RateLimitPartitionKey Tenant(int index)
        => RateLimitPartitionKey.ForTenant(RateLimitGroups.Management, $"tenant-{index}");

    private static ValueTask<FixedWindowDecision?> Count(DeploymentRateLimiter limiter, RateLimitPartitionKey key, int limit = 3)
        => limiter.TryCountAsync(key, limit, Minute, RateLimiterKind.Endpoint, CancellationToken.None);

    [Fact]
    public async Task The_in_memory_counter_removes_the_entries_whose_window_has_ended()
    {
        var clock = new ManualTimeProvider(Start.AddSeconds(15));
        var store = new InMemoryRateLimitCounterStore();
        var limiter = new DeploymentRateLimiter(store, clock);

        for (var tenant = 0; tenant < 3_000; tenant++)
        {
            (await Count(limiter, Tenant(tenant))).ShouldNotBeNull().Allowed.ShouldBeTrue();
        }

        store.TrackedPartitions.ShouldBe(3_000);

        clock.Advance(TimeSpan.FromMinutes(3));
        (await Count(limiter, Tenant(-1))).ShouldNotBeNull().Allowed.ShouldBeTrue();

        store.TrackedPartitions.ShouldBe(1, "only the partition that is counting in an open window has an entry");
    }

    [Fact]
    public async Task A_window_that_is_still_open_keeps_counting_through_a_sweep()
    {
        var clock = new ManualTimeProvider(Start.AddSeconds(15));
        var store = new InMemoryRateLimitCounterStore();
        var limiter = new DeploymentRateLimiter(store, clock);

        (await Count(limiter, Tenant(1))).ShouldNotBeNull().Allowed.ShouldBeTrue();

        // 12:01:20: the first sweep's next time (12:01:15) has passed, the first tenant's window (12:00) is over
        // and the second tenant opens one (12:01) that is not.
        clock.Advance(TimeSpan.FromSeconds(65));
        var key = Tenant(2);

        (await Count(limiter, key, limit: 2)).ShouldNotBeNull().Allowed.ShouldBeTrue();
        store.TrackedPartitions.ShouldBe(1, "the first tenant's window is over");

        clock.Advance(TimeSpan.FromSeconds(10));
        (await Count(limiter, key, limit: 2)).ShouldNotBeNull().Allowed.ShouldBeTrue();
        (await Count(limiter, key, limit: 2)).ShouldNotBeNull().Allowed.ShouldBeFalse();
    }

    [Fact]
    public async Task A_window_that_starts_ahead_of_this_clock_stays_until_real_time_reaches_its_end()
    {
        var store = new InMemoryRateLimitCounterStore();

        // Another instance's clock ran ahead: the stored window is not over, and the count it holds is the one the
        // partition is held to.
        await store.IncrementAsync("endpoint:tracon:tenant:acme", Start.AddMinutes(10));

        store.RemoveEndedWindows(Start, Minute);

        store.TrackedPartitions.ShouldBe(1);
    }

    [Fact]
    public async Task A_longer_window_than_the_one_first_seen_is_never_cut_short()
    {
        var clock = new ManualTimeProvider(Start.AddSeconds(15));
        var store = new InMemoryRateLimitCounterStore();
        var limiter = new DeploymentRateLimiter(store, clock);
        var hour = TimeSpan.FromHours(1);

        // One partition counts per minute, another per hour, and the limiter has seen both.
        (await limiter.TryCountAsync(Tenant(1), 5, Minute, RateLimiterKind.Endpoint, CancellationToken.None)).ShouldNotBeNull();
        (await limiter.TryCountAsync(Tenant(2), 2, hour, RateLimiterKind.Endpoint, CancellationToken.None)).ShouldNotBeNull();

        // Ten minutes on: the minute window is over, the hour window is not and is still counting.
        clock.Advance(TimeSpan.FromMinutes(10));
        (await limiter.TryCountAsync(Tenant(3), 5, Minute, RateLimiterKind.Endpoint, CancellationToken.None)).ShouldNotBeNull();

        (await limiter.TryCountAsync(Tenant(2), 2, hour, RateLimiterKind.Endpoint, CancellationToken.None)).ShouldNotBeNull().Allowed.ShouldBeTrue();
        (await limiter.TryCountAsync(Tenant(2), 2, hour, RateLimiterKind.Endpoint, CancellationToken.None)).ShouldNotBeNull().Allowed.ShouldBeFalse();
    }

    [Fact]
    public async Task The_warning_maps_forget_the_partitions_that_were_warned_about_longer_ago_than_the_interval()
    {
        var clock = new ManualTimeProvider(Start);
        var limiter = new DeploymentRateLimiter(new FailingStore(), clock);

        for (var tenant = 0; tenant < 3_000; tenant++)
        {
            (await Count(limiter, Tenant(tenant))).ShouldBeNull();
        }

        limiter.WarnedPartitions.ShouldBe(3_000);

        clock.Advance(DeploymentRateLimiter.WarningInterval + TimeSpan.FromSeconds(1));
        (await Count(limiter, Tenant(-1))).ShouldBeNull();

        limiter.WarnedPartitions.ShouldBe(1);
    }

    private sealed class FailingStore : IRateLimitCounterStore
    {
        public ValueTask<RateLimitWindowCount> IncrementAsync(
            string partitionKey,
            DateTimeOffset windowStart,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The database is down.");
    }
}
