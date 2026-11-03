using static Tracon.Core.UnitTests.Scheduling.JobAttemptProbes;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// The slots of a job worker: one pool per lane that has a budget, and the shared pool.
/// </summary>
/// <remarks>
/// The pool itself is held by <c>SlotPoolTests</c>; the stop of the whole worker against a job that waits for its
/// previous attempt is <see cref="JobWorkerStopDropsNoTaskTests"/>.
/// </remarks>
public sealed class JobWorkerSlotsTests
{
    [Fact]
    public async Task The_workers_slots_end_only_when_the_slot_of_every_lane_is_back()
    {
        var slots = new JobWorkerSlots(1);
        var token = TestContext.Current.CancellationToken;

        (await slots.Shared.TryAcquireAsync(token)).ShouldBeTrue();
        (await slots.ForLane("batch", 1).TryAcquireAsync(token)).ShouldBeTrue();

        slots.Close();
        slots.Shared.Release();

        slots.WaitForDrainedAsync().IsCompleted.ShouldBeFalse("the batch lane's slot is still out");

        slots.ForLane("batch", 1).Release();

        await slots.WaitForDrainedAsync().WaitAsync(WaitUntil.DefaultTimeout, token);
    }

    [Fact]
    public async Task A_stop_that_cancels_the_lease_call_still_ends_the_worker()
    {
        // The slot is taken before the lease call. A stop cancels that call with an exception the
        // lease loop does not treat as a failure; the slot must go back anyway or the worker
        // waits for it for ever.
        var store = new ProbeJobStore(new InMemoryJobStore());
        var leasing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        store.BeforeLease = async token =>
        {
            leasing.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false); // delay: simulated
        };

        var (worker, host) = Worker(store, new TokenProbeHandler());

        using (host)
        using (worker)
        {
            await worker.StartAsync(TestContext.Current.CancellationToken);
            await leasing.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            await worker.StopAsync(CancellationToken.None).WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
        }
    }
}
