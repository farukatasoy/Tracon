using static Tracon.Core.UnitTests.Scheduling.JobAttemptProbes;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// The pool a job worker takes its slots from outlives every slot still out.
/// </summary>
/// <remarks>
/// The worker's own shutdown races decide whether the failure shows (see
/// <see cref="JobWorkerStopDropsNoTaskTests"/>); these tests hold the same rules with no race.
/// </remarks>
public sealed class JobSlotPoolTests
{
    [Fact]
    public async Task A_slot_returned_after_the_pool_was_disposed_is_accepted_and_ends_the_pool()
    {
        var pool = new JobSlotPool(1);

        (await pool.TryAcquireAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();

        pool.Dispose();

        pool.Drained.IsCompleted.ShouldBeFalse("a slot is still out");

        Should.NotThrow(pool.Release);

        pool.Drained.IsCompletedSuccessfully.ShouldBeTrue("the last slot ended the pool");
    }

    [Fact]
    public async Task The_pool_ends_with_its_last_slot_and_not_with_the_first_one_returned()
    {
        var pool = new JobSlotPool(2);

        (await pool.TryAcquireAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();
        (await pool.TryAcquireAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();

        pool.Dispose();
        pool.Release();

        pool.Drained.IsCompleted.ShouldBeFalse("one slot is still out");

        pool.Release();

        pool.Drained.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task A_disposed_pool_hands_out_no_slot()
    {
        var pool = new JobSlotPool(1);

        pool.Dispose();

        (await pool.TryAcquireAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
        pool.Drained.IsCompletedSuccessfully.ShouldBeTrue("a pool nobody holds a slot of ends when it is disposed");
    }

    [Fact]
    public async Task A_full_pool_refuses_without_waiting_and_a_returned_slot_is_taken_again()
    {
        var pool = new JobSlotPool(1);

        (await pool.TryAcquireAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();
        (await pool.TryAcquireAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();

        pool.Release();

        (await pool.TryAcquireAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();

        pool.Release();
        pool.Dispose();
    }

    [Fact]
    public async Task An_acquire_that_throws_does_not_keep_the_pool_from_ending()
    {
        var pool = new JobSlotPool(1);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await pool.TryAcquireAsync(canceled.Token));

        pool.Dispose();

        pool.Drained.IsCompletedSuccessfully.ShouldBeTrue("the failed acquire took no slot");
    }

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
