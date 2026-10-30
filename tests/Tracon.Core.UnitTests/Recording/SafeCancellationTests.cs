namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// <c>LiveVoiceSessionHost.StopDelegationsAsync</c> cancelled a snapshot of
/// sources that finished delegations dispose; one disposed source aborted the close.
/// The interleaving cannot be forced through the host, so the snapshot is built by hand.
/// </summary>
public sealed class SafeCancellationTests
{
    [Fact]
    public void A_live_source_is_cancelled_and_reported()
    {
        using var source = new CancellationTokenSource();

        source.TryCancel().ShouldBeTrue();

        source.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public void A_disposed_source_is_reported_not_thrown()
    {
        var source = new CancellationTokenSource();
        source.Dispose();

        source.TryCancel().ShouldBeFalse();
    }

    [Fact]
    public async Task A_snapshot_with_a_disposed_source_still_cancels_every_live_one()
    {
        // The disposed source sits FIRST: the loop must keep going past it.
        var finished = new CancellationTokenSource();
        using var before = new CancellationTokenSource();
        using var after = new CancellationTokenSource();
        finished.Dispose();

        await SafeCancellation.CancelAllAsync([finished, before, after]);

        before.IsCancellationRequested.ShouldBeTrue();
        after.IsCancellationRequested.ShouldBeTrue();
    }
}
