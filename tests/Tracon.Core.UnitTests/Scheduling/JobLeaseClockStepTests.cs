using Tracon.Core.UnitTests.Fakes;
using static Tracon.Core.UnitTests.Scheduling.JobAttemptProbes;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// What a job worker does with its lease when the wall clock and the monotonic clock part ways: the wall clock stepped
/// (a time service correction, a resumed virtual machine) while the monotonic clock goes on, and the monotonic clock
/// jumped (a suspension a platform counts) while the wall clock did not.
/// </summary>
/// <remarks>
/// <para>
/// The worker proves its lease on the monotonic clock: the time since the last renewal it sent, taken before the call.
/// The store decides on the wall clock of the node that reads, which no test of the worker can change; the one store
/// here shares the worker's clock, so a step that reaches the worker reaches the store the same way. These tests hold
/// ONE slot, so the worker's own poll cannot take the running job again: what they ask of is the worker's renewal. The
/// store's wall-clock expiry is the probe <c>JobLeaseWallStepProbe</c>.
/// </para>
/// <para>
/// The renewal timer is the system's (the fake does not override <c>CreateTimer</c>), so renewals happen in real time
/// while the clocks are moved by the test. A lease of 900 ms renews every 300 ms; the tests wait for a number of
/// renewals, never for a span of time.
/// </para>
/// </remarks>
public sealed class JobLeaseClockStepTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromMilliseconds(900);

    [Fact]
    public async Task A_wall_clock_stepped_forward_and_back_does_not_cost_the_running_job_its_lease()
    {
        var clock = new ManualTimeProvider();
        var store = new ProbeJobStore(new InMemoryJobStore(clock));
        var handler = new GatedHandler();
        var (worker, host) = Worker(store, handler, OneSlot, clock: clock);

        using (host)
        using (worker)
        {
            var id = await EnqueueAtAsync(store, clock);

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await handler.Started.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            // Two hours ahead, four hours back (two before the start), forty days ahead: the lease is a duration, so no
            // step is a reason to stop renewing or to tell the handler its lease is gone.
            foreach (var step in new[] { TimeSpan.FromHours(2), TimeSpan.FromHours(-4), TimeSpan.FromDays(40) })
            {
                clock.StepWallClock(step);

                var renewals = store.Renewals;

                await WaitUntil.TrueAsync(() => store.Renewals >= renewals + 3, $"three renewals after the wall clock stepped by {step}");
                handler.Cancelled.ShouldBeFalse($"the handler's token fell after the wall clock stepped by {step}");
            }

            handler.Release.TrySetResult();

            var job = await WaitForAsync(store, id, JobStatus.Completed, JobStatus.Failed);

            await worker.StopAsync(TestContext.Current.CancellationToken);

            job.Status.ShouldBe(JobStatus.Completed);
            job.Attempt.ShouldBe(1, "the job was not leased a second time");
            handler.Calls.ShouldBe(1);

            // The completion time is a stamp of the clock the host registered: it records, and decides nothing.
            job.CompletedAt.ShouldBe(clock.GetUtcNow());
        }
    }

    [Fact]
    public async Task A_monotonic_jump_past_the_lease_with_a_healthy_store_still_lets_the_job_finish()
    {
        var clock = new ManualTimeProvider();
        var store = new ProbeJobStore(new InMemoryJobStore(clock));
        var handler = new GatedHandler();
        var (worker, host) = Worker(store, handler, OneSlot, clock: clock);

        using (host)
        using (worker)
        {
            var id = await EnqueueAtAsync(store, clock);

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await handler.Started.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            // The process was suspended for longer than the lease and the counter counted it; the wall clock did not move.
            // The store still answers every renewal, so the lease is held: the job has to finish, once.
            clock.StepMonotonic(TimeSpan.FromSeconds(5));

            var renewals = store.Renewals;

            await WaitUntil.TrueAsync(() => store.Renewals >= renewals + 3, "three renewals after the monotonic jump");
            handler.Cancelled.ShouldBeFalse("a renewal the store answered is a lease held, whatever the counter says");

            handler.Release.TrySetResult();

            var job = await WaitForAsync(store, id, JobStatus.Completed, JobStatus.Failed);

            await worker.StopAsync(TestContext.Current.CancellationToken);

            job.Status.ShouldBe(JobStatus.Completed);
            job.Attempt.ShouldBe(1);
            handler.Calls.ShouldBe(1);
        }
    }

    [Fact]
    public async Task A_monotonic_jump_past_the_lease_with_a_store_that_cannot_be_reached_tells_the_handler_its_lease_is_gone()
    {
        // The safe direction: a counter that says the lease ran out, and a store that cannot confirm it, end the attempt at
        // the next tick instead of letting the handler run on a lease another worker may hold.
        var clock = new ManualTimeProvider();
        var store = new ProbeJobStore(new InMemoryJobStore(clock));
        var handler = new GatedHandler();
        var (worker, host) = Worker(store, handler, OneSlot, clock: clock);

        using (host)
        using (worker)
        {
            await EnqueueAtAsync(store, clock);

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await handler.Started.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            handler.Cancelled.ShouldBeFalse();

            clock.StepMonotonic(TimeSpan.FromSeconds(5));
            store.RenewalFailure = new IOException("the job store is unreachable (probe)");

            await WaitUntil.TrueAsync(() => handler.Cancelled, "the handler's token to fall once the lease could no longer be proven");

            store.RenewalFailure = null;
            handler.Release.TrySetResult();

            await worker.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task The_same_store_outage_without_a_monotonic_jump_keeps_the_handler_running_until_the_lease_could_really_have_run_out()
    {
        // The other side of the test above: nothing but the jump differs, so the jump is what ended the attempt.
        var clock = new ManualTimeProvider();
        var store = new ProbeJobStore(new InMemoryJobStore(clock));
        var handler = new GatedHandler();
        var (worker, host) = Worker(store, handler, OneSlot, clock: clock);

        using (host)
        using (worker)
        {
            await EnqueueAtAsync(store, clock);

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await handler.Started.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            store.RenewalFailure = new IOException("the job store is unreachable (probe)");

            var renewals = store.Renewals;

            // The monotonic clock is fixed, so the lease cannot run out whatever the number of failed renewals.
            await WaitUntil.TrueAsync(() => store.Renewals >= renewals + 5, "five failed renewals");

            handler.Cancelled.ShouldBeFalse("no time passed on the monotonic clock, so the lease is still provable");

            store.RenewalFailure = null;
            handler.Release.TrySetResult();

            await worker.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-2)]
    public async Task A_wall_clock_step_with_a_store_that_cannot_be_reached_does_not_tell_the_handler_its_lease_is_gone(int hours)
    {
        // The test that holds the lease to the monotonic clock: renewals fail, the wall clock moves by hours, and no time
        // has passed on the clock the worker counts the lease on, so the lease is still provable and the handler keeps
        // running. A worker that counted on the wall clock would give a lease up that it holds (a step ahead) or keep
        // one that is gone (a step back).
        var clock = new ManualTimeProvider();
        var store = new ProbeJobStore(new InMemoryJobStore(clock));
        var handler = new GatedHandler();
        var (worker, host) = Worker(store, handler, OneSlot, clock: clock);

        using (host)
        using (worker)
        {
            await EnqueueAtAsync(store, clock);

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await handler.Started.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            clock.StepWallClock(TimeSpan.FromHours(hours));
            store.RenewalFailure = new IOException("the job store is unreachable (probe)");

            var renewals = store.Renewals;

            await WaitUntil.TrueAsync(() => store.Renewals >= renewals + 5, "five failed renewals after the wall clock stepped");

            handler.Cancelled.ShouldBeFalse("the lease is a duration on the monotonic clock, and none has passed");

            store.RenewalFailure = null;
            handler.Release.TrySetResult();

            await worker.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    private static void OneSlot(TraconSchedulingOptions options)
    {
        options.LeaseDuration = Lease;
        options.MaxConcurrentJobs = 1;
    }

    private static async Task<Guid> EnqueueAtAsync(ProbeJobStore store, TimeProvider clock)
    {
        var id = Guid.NewGuid();
        var now = clock.GetUtcNow();

        await store.EnqueueAsync(
            new JobRecord
            {
                Id = id,
                TenantId = Tenant,
                HandlerKey = JobHandlerKeys.AgentBatch,
                TargetName = "clock-step",
                Status = JobStatus.Pending,
                MaxAttempts = 3,
                ScheduledFor = now,
                CreatedAt = now,
            },
            [],
            TestContext.Current.CancellationToken);

        return id;
    }

    /// <summary>A handler that runs until the test releases it or its token falls, and notes whether the token had fallen when it left.</summary>
    private sealed class GatedHandler : IJobHandler
    {
        private int _calls;
        private int _cancelled;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls => Volatile.Read(ref _calls);

        public bool Cancelled => Volatile.Read(ref _cancelled) != 0;

        public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            Started.TrySetResult();

            try
            {
                await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                // Read on the way out, not through a registration: a registration disposed by the exit can be
                // removed before its callback ran.
                if (cancellationToken.IsCancellationRequested)
                {
                    Interlocked.Exchange(ref _cancelled, 1);
                }
            }
        }
    }
}
