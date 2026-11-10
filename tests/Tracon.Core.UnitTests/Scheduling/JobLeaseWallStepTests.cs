using Tracon.Core.UnitTests.Fakes;
using static Tracon.Core.UnitTests.Scheduling.JobAttemptProbes;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// What an in-process job store does with a running job's lease when the wall clock it reads steps forward past the lease
/// while the worker that holds the lease renews on its own monotonic clock.
/// </summary>
/// <remarks>
/// <para>
/// A lease is a span of time, so the store decides when it ends on a monotonic reading, not on the wall clock: a wall clock
/// that a time service steps forward by two hours (or a resumed virtual machine) must not make a lease that was taken a
/// moment ago look over. Before the store read a monotonic clock, the second slot of a worker leased the running job at
/// its first poll after the step, before the first renewal after the step had been answered (<c>handlerCalls=2
/// attempt=2</c>), and the job ran twice at once.
/// </para>
/// <para>
/// The scenarios hold TWO slots: the second one polls and sees what the store shows it. Tests that hold one slot
/// (<c>JobLeaseClockStepTests</c>) cannot see the lease being taken again.
/// </para>
/// </remarks>
public sealed class JobLeaseWallStepTests
{
    [Fact]
    public async Task A_wall_clock_stepped_past_the_lease_does_not_let_a_second_slot_lease_the_job_that_is_still_running()
    {
        // The worker's poll and renewal tick on real timers while the test steps the wall clock under them.
        var clock = ManualClock.WithSystemTimers(systemTimestamp: false);
        var store = new ProbeJobStore(new InMemoryJobStore(clock));
        var handler = new CountingHandler();

        // Two slots: the second one polls and sees what the store shows it.
        var (worker, host) = Worker(
            store,
            handler,
            o =>
            {
                o.LeaseDuration = TimeSpan.FromMilliseconds(900);
                o.MaxConcurrentJobs = 2;
            },
            clock: clock);

        using (host)
        using (worker)
        {
            var now = clock.GetUtcNow();
            var id = Guid.NewGuid();

            await store.EnqueueAsync(
                new JobRecord
                {
                    Id = id,
                    TenantId = Tenant,
                    HandlerKey = JobHandlerKeys.AgentBatch,
                    TargetName = "wall-step",
                    Status = JobStatus.Pending,
                    MaxAttempts = 3,
                    ScheduledFor = now,
                    CreatedAt = now,
                },
                [],
                TestContext.Current.CancellationToken);

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await handler.Started.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            // The clock the store reads goes two hours ahead. The job is running and its worker renews.
            clock.StepWallClock(TimeSpan.FromHours(2));

            await WaitUntil.TrueAsync(() => handler.Calls >= 2 || store.Renewals >= 6, "a second lease, or six renewals");

            var calls = handler.Calls;
            var job = await store.GetAsync(Tenant, id, TestContext.Current.CancellationToken);

            handler.Release.TrySetResult();
            await worker.StopAsync(TestContext.Current.CancellationToken);

            job.ShouldNotBeNull().Attempt.ShouldBe(1, "the lease was never taken again");

            // The invariant a store owes the worker that renews: the job it is running is not leased to anyone else.
            calls.ShouldBe(1, "the running job was leased again because the store read a wall clock past the lease");
        }
    }

    private sealed class CountingHandler : IJobHandler
    {
        private int _calls;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls => Volatile.Read(ref _calls);

        public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            Started.TrySetResult();

            await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
