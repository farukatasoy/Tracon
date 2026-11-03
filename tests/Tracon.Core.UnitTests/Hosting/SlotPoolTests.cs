namespace Tracon.Core.UnitTests.Hosting;

/// <summary>
/// The pool a worker or a runner takes its slots from outlives every slot still out.
/// </summary>
/// <remarks>
/// The shutdown races decide whether the failure shows (<c>JobWorkerStopDropsNoTaskTests</c> for the job worker, the
/// disposal test of <c>WorkflowCancellationSweepTests</c> for the workflow runner); these tests hold the same rules with
/// no race.
/// </remarks>
public sealed class SlotPoolTests
{
    [Fact]
    public async Task A_slot_returned_after_the_pool_was_disposed_is_accepted_and_ends_the_pool()
    {
        var pool = new SlotPool(1);

        (await pool.TryAcquireAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();

        pool.Dispose();

        pool.Drained.IsCompleted.ShouldBeFalse("a slot is still out");

        Should.NotThrow(pool.Release);

        pool.Drained.IsCompletedSuccessfully.ShouldBeTrue("the last slot ended the pool");
    }

    [Fact]
    public async Task The_pool_ends_with_its_last_slot_and_not_with_the_first_one_returned()
    {
        var pool = new SlotPool(2);

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
        var pool = new SlotPool(1);

        pool.Dispose();

        (await pool.TryAcquireAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
        pool.Drained.IsCompletedSuccessfully.ShouldBeTrue("a pool nobody holds a slot of ends when it is disposed");
    }

    [Fact]
    public async Task A_full_pool_refuses_without_waiting_and_a_returned_slot_is_taken_again()
    {
        var pool = new SlotPool(1);

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
        var pool = new SlotPool(1);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await pool.TryAcquireAsync(canceled.Token));

        pool.Dispose();

        pool.Drained.IsCompletedSuccessfully.ShouldBeTrue("the failed acquire took no slot");
    }

    [Fact]
    public async Task A_waiting_acquire_gets_the_slot_a_release_frees_and_one_that_is_canceled_takes_none()
    {
        var pool = new SlotPool(1);
        var token = TestContext.Current.CancellationToken;

        await pool.AcquireAsync(token);
        pool.IsFull.ShouldBeTrue();

        var waiting = pool.AcquireAsync(token).AsTask();

        waiting.IsCompleted.ShouldBeFalse("the only slot is out");

        pool.Release();
        await waiting.WaitAsync(WaitUntil.DefaultTimeout, token);

        using var canceled = new CancellationTokenSource();
        var abandoned = pool.AcquireAsync(canceled.Token).AsTask();

        await canceled.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(async () => await abandoned);

        pool.Release();
        pool.Dispose();

        pool.Drained.IsCompletedSuccessfully.ShouldBeTrue("the canceled wait took no slot, so the pool ended with the one it handed out");
    }

    [Fact]
    public async Task A_closed_pool_refuses_a_waiting_acquire_and_a_slot_held_at_the_close_ends_it_when_returned()
    {
        var pool = new SlotPool(1);
        var token = TestContext.Current.CancellationToken;

        await pool.AcquireAsync(token);
        pool.Dispose();

        await Should.ThrowAsync<ObjectDisposedException>(async () => await pool.AcquireAsync(token));

        pool.Drained.IsCompleted.ShouldBeFalse("a slot is still out");

        pool.Release();

        pool.Drained.IsCompletedSuccessfully.ShouldBeTrue();
    }
}
