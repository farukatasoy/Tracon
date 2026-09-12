namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// Verifies tree cascade behavior at the <see cref="RunCancellationRegistry"/>
/// level: canceling the root propagates to child runs, while canceling a
/// single child affects neither the root nor its sibling branches.
/// </summary>
public sealed class RunCancellationTreeTests
{
    [Fact]
    public void Root_cancellation_cancels_all_child_runs()
    {
        var registry = new RunCancellationRegistry();
        var rootId = TraconId.NewId();
        var childId1 = TraconId.NewId();
        var childId2 = TraconId.NewId();
        var unrelatedRootId = TraconId.NewId();

        using var rootCts = new CancellationTokenSource();
        using var child1Cts = new CancellationTokenSource();
        using var child2Cts = new CancellationTokenSource();
        using var unrelatedCts = new CancellationTokenSource();

        using var rootReg = registry.Register(rootId, rootId, "test", rootCts);
        using var child1Reg = registry.Register(childId1, rootId, "test", child1Cts);
        using var child2Reg = registry.Register(childId2, rootId, "test", child2Cts);
        using var unrelatedReg = registry.Register(unrelatedRootId, unrelatedRootId, "test", unrelatedCts);

        registry.TryCancel(rootId, "test").ShouldBeTrue();

        rootCts.IsCancellationRequested.ShouldBeTrue();
        child1Cts.IsCancellationRequested.ShouldBeTrue();
        child2Cts.IsCancellationRequested.ShouldBeTrue();

        // A different tree that does not share the same RootRunId is unaffected.
        unrelatedCts.IsCancellationRequested.ShouldBeFalse();
    }

    [Fact]
    public void Canceling_a_single_child_run_does_not_affect_the_root_or_sibling_branches()
    {
        var registry = new RunCancellationRegistry();
        var rootId = TraconId.NewId();
        var childId1 = TraconId.NewId();
        var childId2 = TraconId.NewId();

        using var rootCts = new CancellationTokenSource();
        using var child1Cts = new CancellationTokenSource();
        using var child2Cts = new CancellationTokenSource();

        using var rootReg = registry.Register(rootId, rootId, "test", rootCts);
        using var child1Reg = registry.Register(childId1, rootId, "test", child1Cts);
        using var child2Reg = registry.Register(childId2, rootId, "test", child2Cts);

        registry.TryCancel(childId1, "test").ShouldBeTrue();

        child1Cts.IsCancellationRequested.ShouldBeTrue();
        rootCts.IsCancellationRequested.ShouldBeFalse();
        child2Cts.IsCancellationRequested.ShouldBeFalse();
    }
}
