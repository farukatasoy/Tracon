using System.Diagnostics;
using Microsoft.Extensions.Options;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.Tests.Common;

/// <summary>The stores of two nodes over one schema.</summary>
/// <param name="FirstLeases">The first node's single-executor lease store.</param>
/// <param name="SecondLeases">The second node's single-executor lease store.</param>
/// <param name="FirstJobs">The first node's job store.</param>
/// <param name="SecondJobs">The second node's job store.</param>
public sealed record SkewedStores(
    ISingletonLeaseStore FirstLeases,
    ISingletonLeaseStore SecondLeases,
    IJobStore FirstJobs,
    IJobStore SecondJobs);

/// <summary>
/// What the SQL lease stores do when the two nodes that share one database disagree about the time, written once for
/// every SQL provider whose lease statements read the database server's clock.
/// </summary>
/// <remarks>
/// <para>
/// A lease row carries the instant a lease ends, and every node that reads the row decides from it whether the lease is
/// still live. When each node stamped and compared with its OWN clock, two nodes that disagreed about the time disagreed
/// about when a lease ended: a node whose clock ran ahead took a live single-executor lease or a job another node was
/// running, and the owner's result was refused. The scenarios below give the two nodes two manual clocks, shifted from each
/// other by minutes and by an hour in both directions, over one schema, and state what a healthy cluster does: no lease
/// is affected, a lease ends when the database server's clock reaches its end, and a retry delay is measured on the same
/// clock.
/// </para>
/// <para>
/// The manual clocks are handed to the stores (a store that still reads one would show it here) and to the single-executor
/// guards (which only measure elapsed time with them). Time that has to PASS for a lease to end is real time: the
/// database server's clock cannot be moved by a test, so those scenarios use leases of about a second and a half and wait.
/// </para>
/// <para>
/// A provider's test project derives from this class and says one thing: how to open stores over one migrated, empty
/// schema, the first pair reading <c>firstClock</c> and the second pair reading <c>secondClock</c>.
/// </para>
/// </remarks>
public abstract class SqlLeaseUnderClockSkewContract
{
    private static readonly TimeSpan SingletonLeaseDuration = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan JobLeaseDuration = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan ShortLease = TimeSpan.FromMilliseconds(1500);

    private static readonly TimeSpan PollEvery = TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(8);

    /// <summary>The earliest a lease of <see cref="ShortLease"/> can end, measured from before it was written.</summary>
    private static readonly TimeSpan EarliestEnd = TimeSpan.FromMilliseconds(1300);

    private static readonly TimeSpan Step = TimeSpan.FromSeconds(1);

    /// <summary>Opens the stores of two nodes over one migrated, empty schema.</summary>
    /// <param name="firstClock">The clock the first node's stores are handed.</param>
    /// <param name="secondClock">The clock the second node's stores are handed.</param>
    /// <returns>The stores.</returns>
    protected abstract ValueTask<SkewedStores> OpenAsync(TimeProvider firstClock, TimeProvider secondClock);

    /// <summary>
    /// A node whose clock differs from the owner's, by seconds or by an hour in either direction, never takes a live
    /// single-executor lease, and the owner never loses what it holds.
    /// </summary>
    /// <remarks>
    /// The owner renews every third of the lease. The second node polls every second of ITS clock.
    /// </remarks>
    /// <param name="aheadSeconds">How far the second node's clock is ahead of the owner's (negative: behind).</param>
    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(25)]
    [InlineData(31)]
    [InlineData(3600)]
    [InlineData(-3600)]
    public async Task A_node_whose_clock_differs_never_takes_a_live_single_executor_lease(int aheadSeconds)
    {
        var owner = new ManualClock();
        var other = ClockShiftedBy(aheadSeconds);

        var stores = await OpenAsync(owner, other);
        var options = Options();
        var name = $"skew-{Guid.NewGuid():N}";

        var ownerGuard = new SingletonGuard(stores.FirstLeases, options, name, timeProvider: owner);
        var otherGuard = new SingletonGuard(stores.SecondLeases, options, name, timeProvider: other);
        var renewEvery = (int)SingletonGuard.ComputeRenewInterval(SingletonLeaseDuration).TotalSeconds;

        int? otherTookAt = null;
        int? ownerLostAt = null;

        for (var second = 0; second <= 60; second++)
        {
            if (second > 0)
            {
                owner.Advance(Step);
                other.Advance(Step);
            }

            if (second % renewEvery == 0)
            {
                await ownerGuard.TickAsync(TestContext.Current.CancellationToken);
            }

            await otherGuard.TickAsync(TestContext.Current.CancellationToken);

            if (otherTookAt is null && otherGuard.IsHeld)
            {
                otherTookAt = second;
            }

            if (ownerLostAt is null && !ownerGuard.IsHeld)
            {
                ownerLostAt = second;
            }
        }

        Report(
            "single-executor lease",
            $"second node's clock off by {aheadSeconds}s",
            $"took the live lease at {Describe(otherTookAt)}; owner lost it at {Describe(ownerLostAt)}");

        otherTookAt.ShouldBeNull("the lease is live on the database server's clock, whatever the second node's clock says");
        ownerLostAt.ShouldBeNull("the owner keeps what it holds");
    }

    /// <summary>
    /// A single-executor lease ends when the database server's clock reaches its end: not before, whatever the writer's
    /// or the reader's clock says, and not an hour late.
    /// </summary>
    /// <param name="ownerOffsetSeconds">How far the owner's clock is ahead of the other node's (negative: behind).</param>
    [Theory]
    [InlineData(0)]
    [InlineData(3600)]
    [InlineData(-3600)]
    public async Task A_single_executor_lease_ends_when_the_database_clock_says_so(int ownerOffsetSeconds)
    {
        var stores = await OpenAsync(new ShiftedClock(ownerOffsetSeconds), new ShiftedClock(0));
        var name = $"skew-{Guid.NewGuid():N}";

        var clock = Stopwatch.StartNew();

        (await stores.FirstLeases.TryAcquireAsync(name, "owner", ShortLease, TestContext.Current.CancellationToken)).ShouldBeTrue();

        (await stores.SecondLeases.TryAcquireAsync(name, "other", ShortLease, TestContext.Current.CancellationToken))
            .ShouldBeFalse("the lease has just been written and is live");

        var tookAfter = await PollUntilAsync(
            async () => await stores.SecondLeases.TryAcquireAsync(name, "other", ShortLease, TestContext.Current.CancellationToken),
            clock);

        Report(
            "single-executor lease",
            $"owner's clock off by {ownerOffsetSeconds}s, lease {ShortLease.TotalMilliseconds} ms",
            $"the other node took it {Describe(tookAfter)} after the write");

        tookAfter.ShouldNotBeNull("the lease ends on the database server's clock, not an hour away");
        tookAfter.Value.ShouldBeGreaterThanOrEqualTo(EarliestEnd, "the lease must not end before its duration has passed");
    }

    /// <summary>
    /// A node whose clock differs from another node's, by minutes or by an hour in either direction, does not take a job
    /// the other node is RUNNING, and the owner's renewal and completion are accepted.
    /// </summary>
    /// <param name="aheadSeconds">How far the second node's clock is ahead of the first node's (negative: behind).</param>
    [Theory]
    [InlineData(0)]
    [InlineData(150)]
    [InlineData(301)]
    [InlineData(3600)]
    [InlineData(-3600)]
    public async Task A_node_whose_clock_differs_does_not_take_a_job_another_node_is_running(int aheadSeconds)
    {
        var first = new ShiftedClock(0);

        var stores = await OpenAsync(first, new ShiftedClock(aheadSeconds));

        await stores.FirstJobs.EnqueueAsync(Due(), ["a"], TestContext.Current.CancellationToken);

        var leased = (await stores.FirstJobs.LeaseAsync(Request("first"), TestContext.Current.CancellationToken)).ShouldNotBeNull();
        var firstLease = JobLease.Of(leased);

        (await stores.FirstJobs.MarkRunningAsync(firstLease, TestContext.Current.CancellationToken)).ShouldBeTrue();

        var taken = await stores.SecondJobs.LeaseAsync(Request("second"), TestContext.Current.CancellationToken);

        var renewed = await stores.FirstJobs.RenewLeaseAsync(firstLease, JobLeaseDuration, TestContext.Current.CancellationToken);
        var completedByFirst = await stores.FirstJobs.CompleteAsync(firstLease, Completion(first), TestContext.Current.CancellationToken);

        Report(
            "job lease",
            $"second node's clock off by {aheadSeconds}s, job is Running on the first node",
            $"second node {(taken is null ? "did not take the job" : $"took it (attempt {taken.Attempt})")}; " +
            $"first node's renewal {Describe(renewed)}, completion {Describe(completedByFirst)}");

        taken.ShouldBeNull("the job's lease is live on the database server's clock");
        renewed.ShouldBeTrue();
        completedByFirst.ShouldBeTrue();
    }

    /// <summary>
    /// A job's lease ends when the database server's clock reaches its end, whatever the clock of the node that leased it
    /// says, and the first node's writes are refused once another node holds the next attempt.
    /// </summary>
    /// <param name="firstOffsetSeconds">How far the first node's clock is ahead of the second node's (negative: behind).</param>
    [Theory]
    [InlineData(0)]
    [InlineData(3600)]
    [InlineData(-3600)]
    public async Task A_job_lease_ends_when_the_database_clock_says_so(int firstOffsetSeconds)
    {
        var first = new ShiftedClock(firstOffsetSeconds);

        var stores = await OpenAsync(first, new ShiftedClock(0));

        await stores.FirstJobs.EnqueueAsync(Due(), ["a"], TestContext.Current.CancellationToken);

        var clock = Stopwatch.StartNew();

        var leased = (await stores.FirstJobs.LeaseAsync(Request("first", ShortLease), TestContext.Current.CancellationToken)).ShouldNotBeNull();
        var firstLease = JobLease.Of(leased);

        (await stores.SecondJobs.LeaseAsync(Request("second", ShortLease), TestContext.Current.CancellationToken))
            .ShouldBeNull("the lease has just been written and is live");

        JobRecord? taken = null;

        var tookAfter = await PollUntilAsync(
            async () =>
            {
                taken = await stores.SecondJobs.LeaseAsync(Request("second", ShortLease), TestContext.Current.CancellationToken);

                return taken is not null;
            },
            clock);

        Report(
            "job lease",
            $"first node's clock off by {firstOffsetSeconds}s, lease {ShortLease.TotalMilliseconds} ms",
            $"the second node took the job {Describe(tookAfter)} after the lease was written");

        tookAfter.ShouldNotBeNull("the lease ends on the database server's clock, not an hour away");
        tookAfter.Value.ShouldBeGreaterThanOrEqualTo(EarliestEnd, "the lease must not end before its duration has passed");
        taken.ShouldNotBeNull().Attempt.ShouldBe(2, "the take-over counts as another attempt");

        (await stores.FirstJobs.CompleteAsync(firstLease, Completion(first), TestContext.Current.CancellationToken))
            .ShouldBeFalse("the first node's result is refused: the take-over fences it out");
    }

    /// <summary>
    /// A retry delay is measured on the database server's clock: the job is not due before the delay has passed and is due
    /// after it, whatever the clock of the node that released it says.
    /// </summary>
    /// <param name="releaserOffsetSeconds">How far the releasing node's clock is ahead of the leasing node's (negative: behind).</param>
    [Theory]
    [InlineData(0)]
    [InlineData(3600)]
    [InlineData(-3600)]
    public async Task A_retry_delay_is_measured_on_the_database_clock(int releaserOffsetSeconds)
    {
        var stores = await OpenAsync(new ShiftedClock(releaserOffsetSeconds), new ShiftedClock(0));

        await stores.FirstJobs.EnqueueAsync(Due(), ["a"], TestContext.Current.CancellationToken);

        var leased = (await stores.FirstJobs.LeaseAsync(Request("first"), TestContext.Current.CancellationToken)).ShouldNotBeNull();

        var clock = Stopwatch.StartNew();

        (await stores.FirstJobs.ReleaseForRetryAsync(JobLease.Of(leased), "try again", ShortLease, TestContext.Current.CancellationToken))
            .ShouldBeTrue();

        (await stores.SecondJobs.LeaseAsync(Request("second"), TestContext.Current.CancellationToken))
            .ShouldBeNull("the retry delay has just started");

        var dueAfter = await PollUntilAsync(
            async () => await stores.SecondJobs.LeaseAsync(Request("second"), TestContext.Current.CancellationToken) is not null,
            clock);

        Report(
            "retry delay",
            $"releasing node's clock off by {releaserOffsetSeconds}s, delay {ShortLease.TotalMilliseconds} ms",
            $"the job was due {Describe(dueAfter)} after the release");

        dueAfter.ShouldNotBeNull("the delay ends on the database server's clock, not an hour away");
        dueAfter.Value.ShouldBeGreaterThanOrEqualTo(EarliestEnd, "the job must not be due before the delay has passed");
    }

    /// <summary>
    /// A node whose clock runs ahead polling takes none of another node's running jobs, and the other node completes them.
    /// </summary>
    [Fact]
    public async Task A_fast_node_polling_takes_none_of_the_running_jobs_of_another_node()
    {
        var first = new ShiftedClock(0);

        var stores = await OpenAsync(first, new ShiftedClock((int)JobLeaseDuration.TotalSeconds + 1));
        const int Jobs = 3;
        var leases = new List<JobLease>();

        for (var index = 0; index < Jobs; index++)
        {
            await stores.FirstJobs.EnqueueAsync(Due(), [$"item-{index}"], TestContext.Current.CancellationToken);

            var leased = (await stores.FirstJobs.LeaseAsync(Request("first"), TestContext.Current.CancellationToken)).ShouldNotBeNull();
            var lease = JobLease.Of(leased);

            (await stores.FirstJobs.MarkRunningAsync(lease, TestContext.Current.CancellationToken)).ShouldBeTrue();
            leases.Add(lease);
        }

        var attempts = new List<int>();

        for (var poll = 0; poll < Jobs + 2; poll++)
        {
            if (await stores.SecondJobs.LeaseAsync(Request("second"), TestContext.Current.CancellationToken) is { } taken)
            {
                attempts.Add(taken.Attempt);
            }
        }

        var completions = new List<bool>();

        foreach (var lease in leases)
        {
            completions.Add(await stores.FirstJobs.CompleteAsync(lease, Completion(first), TestContext.Current.CancellationToken));
        }

        Report(
            "job lease",
            $"second node's clock ahead by {JobLeaseDuration.TotalSeconds + 1}s polls {Jobs + 2} times, {Jobs} jobs Running on the first node",
            $"took {attempts.Count} jobs; the first node's {Jobs} completions: [{string.Join(", ", completions.Select(Describe))}]");

        attempts.ShouldBeEmpty();
        completions.ShouldAllBe(accepted => accepted);
    }

    private static ManualClock ClockShiftedBy(int seconds)
    {
        var clock = new ManualClock();
        clock.Advance(TimeSpan.FromSeconds(seconds));

        return clock;
    }

    /// <summary>A job that is due on any clock: it was due a minute before the test started.</summary>
    private static JobRecord Due() => TestData.Job(scheduledFor: DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1));

    /// <summary>Polls until <paramref name="attempt"/> says yes, and returns how long after <paramref name="clock"/> started.</summary>
    private static async Task<TimeSpan?> PollUntilAsync(Func<Task<bool>> attempt, Stopwatch clock)
    {
        while (clock.Elapsed < GiveUpAfter)
        {
            await Task.Delay(PollEvery, TestContext.Current.CancellationToken); // delay: product

            if (await attempt())
            {
                return clock.Elapsed;
            }
        }

        return null;
    }

    private static JobLeaseRequest Request(string owner, TimeSpan? duration = null)
        => new() { Owner = owner, LeaseDuration = duration ?? JobLeaseDuration };

    private static JobCompletion Completion(TimeProvider clock)
        => new() { Status = JobStatus.Completed, CompletedAt = clock.GetUtcNow() };

    private static StaticOptionsMonitor<SingletonExecutionOptions> Options()
        => new(new SingletonExecutionOptions { Enabled = true, LeaseDuration = SingletonLeaseDuration });

    private static string Describe(int? seconds) => seconds is { } value ? $"t+{value}s" : "never";

    private static string Describe(TimeSpan? elapsed) => elapsed is { } value ? $"{value.TotalMilliseconds:0} ms" : "never";

    private static string Describe(bool value) => value ? "accepted" : "refused";

    /// <summary>Writes one measured row where a run with detailed output shows it.</summary>
    private static void Report(string subject, string setup, string observed)
        => TestContext.Current.TestOutputHelper?.WriteLine($"[clock skew] {subject} | {setup} | {observed}");

    /// <summary>The system clock moved by a fixed amount: it keeps running, and it is wrong by that amount.</summary>
    /// <param name="offsetSeconds">How far the clock is ahead of the real time (negative: behind).</param>
    private sealed class ShiftedClock(int offsetSeconds) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + TimeSpan.FromSeconds(offsetSeconds);
    }

    /// <summary>An options monitor that returns one value and never tracks changes.</summary>
    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
        where T : class
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
