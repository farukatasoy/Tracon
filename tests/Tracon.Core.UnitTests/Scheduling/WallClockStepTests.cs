using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// What the schedule dispatcher and the single-executor guard do when the wall clock is stepped (a time sync
/// correction, a resumed virtual machine) while the monotonic clock goes on.
/// </summary>
/// <remarks>
/// <para>
/// A schedule is a wall-clock promise ("every five minutes"), so a step moves it: forward, the missed runs collapse
/// into one dispatch (the next run is computed from the new time, never replayed); back, the schedule is silent for the
/// size of the step and then fires once. Neither direction dispatches twice.
/// </para>
/// <para>
/// A lease is a duration, so the guard counts it on the monotonic clock: a wall clock that jumps neither ends a lease the
/// guard holds nor extends one it lost.
/// </para>
/// </remarks>
public sealed class WallClockStepTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(5);

    [Fact]
    public async Task A_wall_clock_stepped_forward_dispatches_a_schedule_once_and_not_once_per_missed_run()
    {
        var clock = new ManualClock(Start);
        var (jobs, schedules, plan, worker) = Build(clock);

        await schedules.SaveAsync(Schedule(Start), TestContext.Current.CancellationToken);
        await worker.StartAsync(TestContext.Current.CancellationToken);

        try
        {
            clock.StepWallClock(TimeSpan.FromHours(3));
            await PollAsync(clock, plan);

            // The claim moves the schedule past the new time: that is the signal the dispatch is over.
            var expectedNext = Start.AddHours(3).AddMinutes(5);
            await WaitUntil.TrueAsync(
                async () => (await schedules.GetAsync("tenant-a", "every-five", TestContext.Current.CancellationToken))!.NextRunAt == expectedNext,
                "the schedule to be claimed for the first run after the new time");

            // Five more polls, each one a step of the poll timer; none of them finds the schedule due again.
            await PollAsync(clock, plan, polls: 5);

            var dispatched = await jobs.QueryAsync(new JobQuery { TenantId = "tenant-a" }, TestContext.Current.CancellationToken);

            dispatched.ShouldHaveSingleItem("thirty-six runs were missed in three hours; they collapse into one dispatch");
        }
        finally
        {
            await worker.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task A_wall_clock_stepped_back_keeps_the_schedule_silent_for_the_step_and_then_dispatches_it_once()
    {
        var clock = new ManualClock(Start);
        var (jobs, schedules, plan, worker) = Build(clock);

        await schedules.SaveAsync(Schedule(Start), TestContext.Current.CancellationToken);
        await worker.StartAsync(TestContext.Current.CancellationToken);

        try
        {
            clock.StepWallClock(TimeSpan.FromHours(-1));

            await PollAsync(clock, plan, polls: 5);

            (await jobs.QueryAsync(new JobQuery { TenantId = "tenant-a" }, TestContext.Current.CancellationToken))
                .ShouldBeEmpty("the stored next run is an hour and five minutes ahead of the stepped-back clock");

            // The clock reaches the stored time: one dispatch.
            clock.StepWallClock(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
            await PollAsync(clock, plan);
            await WaitUntil.TrueAsync(
                async () => (await jobs.QueryAsync(new JobQuery { TenantId = "tenant-a" }, TestContext.Current.CancellationToken)).Count == 1,
                "the schedule to dispatch when the clock reaches its time");

            await PollAsync(clock, plan, polls: 5);

            (await jobs.QueryAsync(new JobQuery { TenantId = "tenant-a" }, TestContext.Current.CancellationToken))
                .Count.ShouldBe(1, "one run, not one per poll");
        }
        finally
        {
            await worker.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task The_single_executor_guard_counts_its_lease_on_the_monotonic_clock()
    {
        var clock = new ManualClock(Start);
        var leaseDuration = TimeSpan.FromSeconds(30);
        var guard = new SingletonGuard(
            new InMemorySingletonLeaseStore(clock),
            new StaticOptionsMonitor<SingletonExecutionOptions>(new SingletonExecutionOptions { Enabled = true, LeaseDuration = leaseDuration }),
            "clock-step",
            timeProvider: clock);

        await guard.TickAsync(TestContext.Current.CancellationToken);
        guard.IsHeld.ShouldBeTrue();

        // The wall clock goes back two hours and forward five: no time passed, so the lease is still the guard's.
        clock.StepWallClock(TimeSpan.FromHours(-2));
        guard.IsHeld.ShouldBeTrue();
        clock.StepWallClock(TimeSpan.FromHours(7));
        guard.IsHeld.ShouldBeTrue();

        // A lease that is not renewed for its whole duration ends, whatever the wall clock says.
        clock.StepWallClock(TimeSpan.FromHours(-5));
        clock.Advance(leaseDuration + TimeSpan.FromSeconds(1));
        guard.IsHeld.ShouldBeFalse();
    }

    /// <summary>Steps the worker's poll timer once for each poll and waits until the worker has asked the store for what is due.</summary>
    private static async Task PollAsync(ManualClock clock, FaultPlan plan, int polls = 1)
    {
        for (var poll = 0; poll < polls; poll++)
        {
            var asked = plan.CountOf(nameof(IJobScheduleStore.ListDueAsync));

            // A step before the worker has armed its timer would be missed.
            await WaitUntil.TrueAsync(() => clock.Pending.Count > 0, "the worker to arm its poll timer");

            clock.Advance(PollInterval);

            await WaitUntil.TrueAsync(() => plan.CountOf(nameof(IJobScheduleStore.ListDueAsync)) > asked, "the worker to poll after the step");
        }
    }

    private static (InMemoryJobStore Jobs, IJobScheduleStore Schedules, FaultPlan Plan, JobWorkerBackgroundService Worker) Build(TimeProvider clock)
    {
        var jobs = new InMemoryJobStore(clock);
        var plan = new FaultPlan();
        var schedules = FaultProxy<IJobScheduleStore>.Create(new InMemoryJobScheduleStore(), plan);
        var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, new ImmediateHandler()));

        var worker = new JobWorkerBackgroundService(
            jobs,
            schedules,
            host.Registry,
            host.Scopes,
            new StaticOptionsMonitor<TraconSchedulingOptions>(new TraconSchedulingOptions
            {
                MaxConcurrentJobs = 1,
                PollInterval = PollInterval,
            }),
            new SchemaReadyGate([]),
            new NotDraining(),
            clock,
            NullLogger<JobWorkerBackgroundService>.Instance);

        return (jobs, schedules, plan, worker);
    }

    private static JobSchedule Schedule(DateTimeOffset now)
        => new()
        {
            TenantId = "tenant-a",
            Name = "every-five",
            HandlerKey = JobHandlerKeys.AgentBatch,
            TargetName = "summarizer",
            Cron = "*/5 * * * *",
            Enabled = true,
            NextRunAt = now.AddMinutes(5),
            CreatedAt = now,
            UpdatedAt = now,
        };

    private sealed class NotDraining : ITraconDrainState
    {
        public bool IsDraining => false;
    }

    private sealed class ImmediateHandler : IJobHandler
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default) => default;
    }
}
