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
/// second and a half, thirty seconds, forty days (past what an <c>int</c> of milliseconds holds) and the longest span a
/// store accepts, which is written whole and not cut. A duration outside that range never reaches the database: the
/// store refuses it first and writes nothing.
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
    /// Server, a little over 68 years. A longer duration is refused by the store.
    /// </summary>
    private const long LongestSpanMilliseconds = 2_147_483_647_000L;

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
    [InlineData(LongestSpanMilliseconds)]
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
    [InlineData(LongestSpanMilliseconds)]
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

    /// <summary>The durations a lease statement cannot be handed: the store refuses each before it builds one.</summary>
    public static TheoryData<TimeSpan> RefusedDurations => new()
    {
        TimeSpan.Zero,
        TimeSpan.FromMilliseconds(-1),
        Timeout.InfiniteTimeSpan,
        TimeSpan.FromMilliseconds(LongestSpanMilliseconds + 1),
        TimeSpan.MaxValue,
        TimeSpan.MinValue,
    };

    /// <summary>
    /// A single-executor lease is refused a duration it cannot add, and nothing is written. Zero or less used to be written
    /// as a lease that was over at once (which includes <see cref="Timeout.InfiniteTimeSpan"/>: it is minus one
    /// millisecond, not "never"); a duration past what the database adds used to be cut to a little over 68 years and held.
    /// </summary>
    /// <param name="duration">The duration.</param>
    [Theory]
    [MemberData(nameof(RefusedDurations))]
    public async Task A_single_executor_lease_of_an_edge_duration_is_refused_and_writes_no_row(TimeSpan duration)
    {
        var (leases, _) = await OpenAsync();
        var name = $"edge-{Guid.NewGuid():N}";

        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            async () => await leases.TryAcquireAsync(name, "a", duration, TestContext.Current.CancellationToken));

        // No row of a long lease is left to hold the name: the next owner takes it, and its lease is its own.
        (await leases.TryAcquireAsync(name, "b", SingletonLease, TestContext.Current.CancellationToken)).ShouldBeTrue();
        var expectedMicroseconds = (long)SingletonLease.TotalMilliseconds * 1000;

        (await SingletonLeaseSpanMicrosecondsAsync(name)).ShouldBeInRange(expectedMicroseconds - 1000, expectedMicroseconds + 1000);
    }

    /// <summary>
    /// A job lease is refused the same durations the same way: nothing is written and no attempt is spent, so the next
    /// worker leases the job as attempt one.
    /// </summary>
    /// <param name="duration">The duration.</param>
    [Theory]
    [MemberData(nameof(RefusedDurations))]
    public async Task A_job_lease_of_an_edge_duration_is_refused_and_spends_no_attempt(TimeSpan duration)
    {
        var (_, jobs) = await OpenAsync();
        var job = TestData.Job();

        await jobs.EnqueueAsync(job, ["a"], TestContext.Current.CancellationToken);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            async () => await jobs.LeaseAsync(new JobLeaseRequest { Owner = "a", LeaseDuration = duration }, TestContext.Current.CancellationToken));

        var second = await jobs.LeaseAsync(
            new JobLeaseRequest { Owner = "b", LeaseDuration = TimeSpan.FromMinutes(5) },
            TestContext.Current.CancellationToken);

        second.ShouldNotBeNull().Attempt.ShouldBe(1, "the refused call leased nothing");
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
