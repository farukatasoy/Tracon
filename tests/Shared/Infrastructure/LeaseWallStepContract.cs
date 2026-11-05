using Tracon.Testing.Contracts.Storage;

namespace Tracon.Tests.Common;

/// <summary>
/// What the lease stores that read the application's clock (the in-memory stores and SQLite, whose file one process uses) do
/// when the wall clock steps while a lease is held, written once for every such store.
/// </summary>
/// <remarks>
/// <para>
/// A lease is a span of time, so the end of a lease is decided on a monotonic reading: a wall clock that a time service steps
/// forward by hours, or a resumed virtual machine, must not make a lease that was taken a moment ago look over (the job would
/// run twice at once, or two guards would believe they are the one executor), and a wall clock stepped back must not keep a
/// lease alive past the span it was taken for. A promise made on the calendar is the other way round: a job scheduled for a
/// time is ready when the wall clock reaches it.
/// </para>
/// <para>
/// PostgreSQL and SQL Server read the database server's clock inside the lease statements, so a step of a node's wall clock
/// never reaches their leases; the clock-skew contracts have those scenarios.
/// </para>
/// </remarks>
public abstract class LeaseWallStepContract
{
    private static readonly TimeSpan JobLease = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan SingletonLease = TimeSpan.FromSeconds(30);

    /// <summary>Opens the stores over one empty schema, reading <paramref name="clock"/>.</summary>
    /// <param name="clock">The application clock the stores read.</param>
    /// <returns>The job store and the single-executor lease store.</returns>
    protected abstract ValueTask<(IJobStore Jobs, ISingletonLeaseStore Leases)> OpenAsync(TimeProvider clock);

    /// <summary>A running job is not leased again, and its lease can be renewed, after the wall clock steps past the lease.</summary>
    [Fact]
    public async Task A_wall_clock_stepped_past_a_job_lease_does_not_end_the_lease()
    {
        var clock = new ManualClock();
        var (jobs, _) = await OpenAsync(clock);
        var job = await EnqueueAsync(jobs, clock);

        var leased = await LeaseAsync(jobs, "first");
        leased.ShouldNotBeNull().Id.ShouldBe(job.Id);

        clock.StepWallClock(TimeSpan.FromHours(2));

        (await LeaseAsync(jobs, "second")).ShouldBeNull("the running job is not leased again because the wall clock stepped");
        (await jobs.RenewLeaseAsync(new JobLease { JobId = job.Id, Owner = "first", Attempt = 1 }, JobLease, TestContext.Current.CancellationToken))
            .ShouldBeTrue("the owner still holds the lease");
    }

    /// <summary>A wall clock stepped back does not keep a lease alive once the time it was taken for has passed.</summary>
    [Fact]
    public async Task A_wall_clock_stepped_back_does_not_keep_a_job_lease_alive_once_its_time_has_passed()
    {
        var clock = new ManualClock();
        var (jobs, _) = await OpenAsync(clock);
        await EnqueueAsync(jobs, clock);

        (await LeaseAsync(jobs, "first")).ShouldNotBeNull();

        clock.StepWallClock(-TimeSpan.FromHours(1));
        clock.Advance(JobLease + TimeSpan.FromSeconds(1));

        var again = await LeaseAsync(jobs, "second");

        again.ShouldNotBeNull("the lease ended five minutes after it was taken, whatever the wall clock reads");
        again.Attempt.ShouldBe(2);
    }

    /// <summary>A lease still ends when the time it was taken for has passed (the monotonic reading is not a longer lease).</summary>
    [Fact]
    public async Task A_job_lease_ends_when_the_time_it_was_taken_for_has_passed()
    {
        var clock = new ManualClock();
        var (jobs, _) = await OpenAsync(clock);
        await EnqueueAsync(jobs, clock);

        (await LeaseAsync(jobs, "first")).ShouldNotBeNull();

        clock.Advance(JobLease - TimeSpan.FromSeconds(1));
        (await LeaseAsync(jobs, "second")).ShouldBeNull("the lease has a second left");

        clock.Advance(TimeSpan.FromSeconds(2));
        (await LeaseAsync(jobs, "second")).ShouldNotBeNull().Attempt.ShouldBe(2);
    }

    /// <summary>A job scheduled for a time is ready when the wall clock reaches it: a promise on the calendar follows the wall clock.</summary>
    [Fact]
    public async Task A_job_scheduled_for_later_is_ready_when_the_wall_clock_reaches_it()
    {
        var clock = new ManualClock();
        var (jobs, _) = await OpenAsync(clock);
        var job = await EnqueueAsync(jobs, clock, scheduledFor: clock.GetUtcNow() + TimeSpan.FromMinutes(30));

        (await LeaseAsync(jobs, "first")).ShouldBeNull("the job is scheduled for half an hour from now");

        clock.StepWallClock(TimeSpan.FromHours(1));

        (await LeaseAsync(jobs, "first")).ShouldNotBeNull().Id.ShouldBe(job.Id);
    }

    /// <summary>The ceiling on the jobs a tenant holds counts a lease that is alive on the monotonic reading, not the wall clock.</summary>
    [Fact]
    public async Task A_wall_clock_stepped_past_a_job_lease_does_not_lower_the_count_a_tenant_ceiling_sees()
    {
        var clock = new ManualClock();
        var (jobs, _) = await OpenAsync(clock);

        await EnqueueAsync(jobs, clock);
        await EnqueueAsync(jobs, clock);

        var cap = new JobTenantCap { HandlerKeys = [JobHandlerKeys.AgentBatch], MaxLeasedPerTenant = 1 };

        (await jobs.LeaseAsync(Request("first", cap), TestContext.Current.CancellationToken)).ShouldNotBeNull();

        clock.StepWallClock(TimeSpan.FromHours(2));

        (await jobs.LeaseAsync(Request("second", cap), TestContext.Current.CancellationToken))
            .ShouldBeNull("the tenant holds the one lease its ceiling allows, and that lease did not end");
    }

    /// <summary>A single-executor lease is not taken, and can be renewed, after the wall clock steps past it.</summary>
    [Fact]
    public async Task A_wall_clock_stepped_past_a_single_executor_lease_does_not_end_the_lease()
    {
        var clock = new ManualClock();
        var (_, leases) = await OpenAsync(clock);

        (await leases.TryAcquireAsync("step", "first", SingletonLease, TestContext.Current.CancellationToken)).ShouldBeTrue();

        clock.StepWallClock(TimeSpan.FromHours(1));

        (await leases.TryAcquireAsync("step", "second", SingletonLease, TestContext.Current.CancellationToken))
            .ShouldBeFalse("the lease did not end: a wall clock step does not end a lease");
        (await leases.RenewAsync("step", "first", SingletonLease, TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    /// <summary>A single-executor lease still ends when the time it was taken for has passed, whatever the wall clock reads.</summary>
    [Fact]
    public async Task A_single_executor_lease_ends_when_the_time_it_was_taken_for_has_passed()
    {
        var clock = new ManualClock();
        var (_, leases) = await OpenAsync(clock);

        (await leases.TryAcquireAsync("step", "first", SingletonLease, TestContext.Current.CancellationToken)).ShouldBeTrue();

        clock.StepWallClock(-TimeSpan.FromHours(1));
        clock.Advance(SingletonLease - TimeSpan.FromSeconds(1));
        (await leases.TryAcquireAsync("step", "second", SingletonLease, TestContext.Current.CancellationToken)).ShouldBeFalse("the lease has a second left");

        clock.Advance(TimeSpan.FromSeconds(2));
        (await leases.TryAcquireAsync("step", "second", SingletonLease, TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    private static async Task<JobRecord> EnqueueAsync(IJobStore jobs, ManualClock clock, DateTimeOffset? scheduledFor = null)
    {
        var job = TestData.Job(scheduledFor: scheduledFor ?? clock.GetUtcNow() - TimeSpan.FromMinutes(1));

        return await jobs.EnqueueAsync(job, ["input"], TestContext.Current.CancellationToken);
    }

    private static JobLeaseRequest Request(string owner, JobTenantCap? cap = null)
        => new() { Owner = owner, LeaseDuration = JobLease, TenantCap = cap };

    private static async Task<JobRecord?> LeaseAsync(IJobStore jobs, string owner)
        => await jobs.LeaseAsync(Request(owner), TestContext.Current.CancellationToken);
}
