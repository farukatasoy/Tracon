using Tracon.Testing.Contracts.Storage;

namespace Tracon.Tests.Common;

/// <summary>
/// How the SQL lease stores that read the database server's clock write a lease's end, and how they read a lease an
/// older version of the code wrote with the node's own clock, written once for every such provider.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The span.</strong> A lease's end is the server's clock plus the duration, passed as a count of milliseconds. The
/// scenarios read <c>lease_until</c> and the instant the same statement stamped (<c>started_at</c> for a job,
/// <c>updated_at</c> for a single-executor lease) back from the table and require the span to be the duration: a duration
/// that is cut to whole seconds, rounded, or lost to the column's precision shows here. They run for a millisecond, a
/// second and a half, thirty seconds and forty days (past what an <c>int</c> of milliseconds holds).
/// </para>
/// <para>
/// <strong>The rolling deployment.</strong> While a deployment rolls, a node of the older version writes a lease whose end it
/// computed from its own clock, and a node of this version reads it against the database server's clock. The scenarios
/// write such a row by hand and pin what the new node does: with a healthy clock on the old node there is no difference (a
/// live lease is live, an over lease is over); with an old node whose clock was an hour fast the row stays live for an hour
/// on the new node, and with one an hour slow the row is already over. That window is the reason the upgrade note asks for
/// the clocks to be checked before the deployment rolls.
/// </para>
/// </remarks>
public abstract class SqlLeaseTimeContract
{
    /// <summary>
    /// The longest span a lease statement is handed, in milliseconds: what <c>DATEADD</c> reaches in whole seconds on SQL
    /// Server, a little over 68 years. A longer duration is cut to it.
    /// </summary>
    private const long LongestSpanMilliseconds = int.MaxValue * 1000L;

    private static readonly TimeSpan SingletonLease = TimeSpan.FromSeconds(30);

    /// <summary>Opens the stores over one migrated, empty schema.</summary>
    /// <returns>The single-executor lease store and the job store.</returns>
    protected abstract ValueTask<(ISingletonLeaseStore Leases, IJobStore Jobs)> OpenAsync();

    /// <summary>Reads, in microseconds, how long after its stamp the job's lease ends.</summary>
    /// <param name="jobId">The job.</param>
    /// <returns>The span between <c>started_at</c> and <c>lease_until</c>.</returns>
    protected abstract ValueTask<long> JobLeaseSpanMicrosecondsAsync(Guid jobId);

    /// <summary>Reads, in microseconds, how long after its stamp the single-executor lease ends.</summary>
    /// <param name="name">The lease.</param>
    /// <returns>The span between <c>updated_at</c> and <c>expires_at</c>.</returns>
    protected abstract ValueTask<long> SingletonLeaseSpanMicrosecondsAsync(string name);

    /// <summary>Writes the row an older node writes when it leases a job: running, with the end it computed itself.</summary>
    /// <param name="jobId">The job, already queued.</param>
    /// <param name="owner">The older node's worker.</param>
    /// <param name="leaseUntil">The end of the lease on the older node's clock.</param>
    /// <returns>The completion task.</returns>
    protected abstract ValueTask WriteOlderNodeJobLeaseAsync(Guid jobId, string owner, DateTimeOffset leaseUntil);

    /// <summary>Writes the row an older node writes when it takes a single-executor lease.</summary>
    /// <param name="name">The lease.</param>
    /// <param name="owner">The older node.</param>
    /// <param name="expiresAt">The end of the lease on the older node's clock.</param>
    /// <returns>The completion task.</returns>
    protected abstract ValueTask WriteOlderNodeSingletonLeaseAsync(string name, string owner, DateTimeOffset expiresAt);

    /// <summary>A job lease ends exactly its duration after it was stamped, down to the millisecond.</summary>
    /// <param name="milliseconds">The duration.</param>
    [Theory]
    [InlineData(1L)]
    [InlineData(1500L)]
    [InlineData(30_000L)]
    [InlineData(86_400_000L * 40)]
    public async Task A_job_lease_lasts_exactly_its_duration(long milliseconds)
    {
        var (_, jobs) = await OpenAsync();
        var job = TestData.Job();

        await jobs.EnqueueAsync(job, ["a"], TestContext.Current.CancellationToken);

        var leased = await jobs.LeaseAsync(
            new JobLeaseRequest { Owner = "w", LeaseDuration = TimeSpan.FromMilliseconds(milliseconds) },
            TestContext.Current.CancellationToken);

        leased.ShouldNotBeNull();

        var span = await JobLeaseSpanMicrosecondsAsync(job.Id);

        span.ShouldBeInRange((milliseconds * 1000) - 1000, (milliseconds * 1000) + 1000);

        // A renewal is measured the same way: from the stamp of its own statement, so the span it leaves is read against
        // the row's end, which is later than the stamp by the new duration less the time that has passed.
        (await jobs.RenewLeaseAsync(JobLease.Of(leased), TimeSpan.FromMilliseconds(milliseconds), TestContext.Current.CancellationToken))
            .ShouldBeTrue();

        var renewed = await JobLeaseSpanMicrosecondsAsync(job.Id);

        renewed.ShouldBeGreaterThanOrEqualTo((milliseconds * 1000) - 1000);
    }

    /// <summary>A single-executor lease ends exactly its duration after it was stamped, down to the millisecond.</summary>
    /// <param name="milliseconds">The duration.</param>
    [Theory]
    [InlineData(1L)]
    [InlineData(1500L)]
    [InlineData(30_000L)]
    [InlineData(86_400_000L * 40)]
    public async Task A_single_executor_lease_lasts_exactly_its_duration(long milliseconds)
    {
        var (leases, _) = await OpenAsync();
        var name = $"span-{Guid.NewGuid():N}";

        (await leases.TryAcquireAsync(name, "a", TimeSpan.FromMilliseconds(milliseconds), TestContext.Current.CancellationToken))
            .ShouldBeTrue();

        (await SingletonLeaseSpanMicrosecondsAsync(name)).ShouldBeInRange((milliseconds * 1000) - 1000, (milliseconds * 1000) + 1000);

        (await leases.RenewAsync(name, "a", TimeSpan.FromMilliseconds(milliseconds), TestContext.Current.CancellationToken))
            .ShouldBeTrue();

        (await SingletonLeaseSpanMicrosecondsAsync(name)).ShouldBeInRange((milliseconds * 1000) - 1000, (milliseconds * 1000) + 1000);
    }

    /// <summary>The durations at the edge of what a lease statement adds, and whether the lease is still held a moment after.</summary>
    public static TheoryData<TimeSpan, bool> EdgeDurations => new()
    {
        { TimeSpan.Zero, false },
        { TimeSpan.FromMilliseconds(-1), false },
        { Timeout.InfiniteTimeSpan, false },
        { TimeSpan.MaxValue, true },
    };

    /// <summary>
    /// A single-executor lease is not given a duration it cannot add. A duration of zero or less is written and the lease is
    /// over at once, which includes <see cref="Timeout.InfiniteTimeSpan"/> (it is minus one millisecond, not "never"); a
    /// duration past what the database adds is cut to a little over 68 years and the lease stays held, instead of the
    /// statement failing with an arithmetic overflow.
    /// </summary>
    /// <param name="duration">The duration.</param>
    /// <param name="heldAfterwards">Whether another owner is refused a moment after the lease is written.</param>
    [Theory]
    [MemberData(nameof(EdgeDurations))]
    public async Task A_single_executor_lease_of_an_edge_duration_is_written_and_cut_to_what_the_database_adds(TimeSpan duration, bool heldAfterwards)
    {
        var (leases, _) = await OpenAsync();
        var name = $"edge-{Guid.NewGuid():N}";

        (await leases.TryAcquireAsync(name, "a", duration, TestContext.Current.CancellationToken))
            .ShouldBeTrue("the duration is not validated: the lease is written");

        await LetTheDatabaseClockPassTheLeaseAsync(duration);

        (await leases.TryAcquireAsync(name, "b", SingletonLease, TestContext.Current.CancellationToken))
            .ShouldBe(!heldAfterwards);

        if (heldAfterwards)
        {
            (await SingletonLeaseSpanMicrosecondsAsync(name))
                .ShouldBeInRange((LongestSpanMilliseconds * 1000) - 1000, (LongestSpanMilliseconds * 1000) + 1000);
        }
    }

    /// <summary>
    /// A lease of zero length ends at the very instant the database wrote it, and the statement that follows asks whether it
    /// ended before <em>now</em>. On a fast machine the two statements can read the same tick of the database clock, and the
    /// answer is "not yet" (measured: about half of the runs, on the commit before this change too). A short wait lets the
    /// clock move; the other edge durations do not need it, as they ended before they were written.
    /// </summary>
    /// <param name="duration">The duration the lease was written with.</param>
    /// <returns>The completion task.</returns>
    private static async Task LetTheDatabaseClockPassTheLeaseAsync(TimeSpan duration)
    {
        if (duration == TimeSpan.Zero)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken); // delay: product
        }
    }

    /// <summary>
    /// A job lease is given the same edge durations the same way: zero or less is leased and over at once, so the next
    /// worker takes the job as another attempt; a duration past what the database adds is cut to a little over 68 years
    /// and the job is not taken again.
    /// </summary>
    /// <param name="duration">The duration.</param>
    /// <param name="heldAfterwards">Whether another worker is refused a moment after the lease is written.</param>
    [Theory]
    [MemberData(nameof(EdgeDurations))]
    public async Task A_job_lease_of_an_edge_duration_is_written_and_cut_to_what_the_database_adds(TimeSpan duration, bool heldAfterwards)
    {
        var (_, jobs) = await OpenAsync();
        var job = TestData.Job();

        await jobs.EnqueueAsync(job, ["a"], TestContext.Current.CancellationToken);

        (await jobs.LeaseAsync(new JobLeaseRequest { Owner = "a", LeaseDuration = duration }, TestContext.Current.CancellationToken))
            .ShouldNotBeNull("the duration is not validated: the lease is written");

        await LetTheDatabaseClockPassTheLeaseAsync(duration);

        var second = await jobs.LeaseAsync(
            new JobLeaseRequest { Owner = "b", LeaseDuration = TimeSpan.FromMinutes(5) },
            TestContext.Current.CancellationToken);

        (second is null).ShouldBe(heldAfterwards);
        second?.Attempt.ShouldBe(2, "the take-over counts as another attempt");

        if (heldAfterwards)
        {
            (await JobLeaseSpanMicrosecondsAsync(job.Id))
                .ShouldBeInRange((LongestSpanMilliseconds * 1000) - 1000, (LongestSpanMilliseconds * 1000) + 1000);
        }
    }

    /// <summary>
    /// A single-executor lease an older node wrote is read against the database server's clock: with a healthy clock on the
    /// older node a live lease is live and an over lease is over; a fast clock keeps it live for the difference, a slow one
    /// has it over already.
    /// </summary>
    /// <param name="olderNodeOffsetSeconds">How far the older node's clock was ahead of the database server's (negative: behind).</param>
    /// <param name="leaseEndsInSeconds">When the older node meant the lease to end, from its own now (negative: it was over).</param>
    /// <param name="expectedTakenByNewNode">Whether the new node takes it now.</param>
    [Theory]
    [InlineData(0, 30, false)]
    [InlineData(0, -2, true)]
    [InlineData(3600, 30, false)]
    [InlineData(-3600, 30, true)]
    public async Task A_single_executor_lease_an_older_node_wrote_is_read_on_the_database_clock(int olderNodeOffsetSeconds, int leaseEndsInSeconds, bool expectedTakenByNewNode)
    {
        var (leases, _) = await OpenAsync();
        var name = $"older-{Guid.NewGuid():N}";
        var olderNow = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(olderNodeOffsetSeconds);

        await WriteOlderNodeSingletonLeaseAsync(name, "older", olderNow + TimeSpan.FromSeconds(leaseEndsInSeconds));

        (await leases.TryAcquireAsync(name, "new", SingletonLease, TestContext.Current.CancellationToken)).ShouldBe(expectedTakenByNewNode);
    }

    /// <summary>
    /// A job lease an older node wrote is read against the database server's clock, in the same way.
    /// </summary>
    /// <param name="olderNodeOffsetSeconds">How far the older node's clock was ahead of the database server's (negative: behind).</param>
    /// <param name="leaseEndsInSeconds">When the older node meant the lease to end, from its own now (negative: it was over).</param>
    /// <param name="expectedTakenByNewNode">Whether the new node takes the job now.</param>
    [Theory]
    [InlineData(0, 300, false)]
    [InlineData(0, -2, true)]
    [InlineData(3600, 300, false)]
    [InlineData(-3600, 300, true)]
    public async Task A_job_lease_an_older_node_wrote_is_read_on_the_database_clock(int olderNodeOffsetSeconds, int leaseEndsInSeconds, bool expectedTakenByNewNode)
    {
        var (_, jobs) = await OpenAsync();
        var job = TestData.Job();
        var olderNow = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(olderNodeOffsetSeconds);

        await jobs.EnqueueAsync(job, ["a"], TestContext.Current.CancellationToken);
        await WriteOlderNodeJobLeaseAsync(job.Id, "older", olderNow + TimeSpan.FromSeconds(leaseEndsInSeconds));

        var taken = await jobs.LeaseAsync(
            new JobLeaseRequest { Owner = "new", LeaseDuration = TimeSpan.FromMinutes(5) },
            TestContext.Current.CancellationToken);

        (taken is not null).ShouldBe(expectedTakenByNewNode);

        taken?.Attempt.ShouldBe(2, "the take-over counts as another attempt");
    }
}
