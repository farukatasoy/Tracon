
namespace Tracon.Core.UnitTests.Recording;

public sealed class RunCancellationRegistryTests
{
    [Fact]
    public void Registering_increases_ActiveCount()
    {
        var registry = new RunCancellationRegistry();
        var runId = TraconId.NewId();

        using var cts = new CancellationTokenSource();
        using var registration = registry.Register(runId, runId, "test", cts);

        registry.ActiveCount.ShouldBe(1);
    }

    [Fact]
    public void Releasing_returns_ActiveCount_to_zero()
    {
        var registry = new RunCancellationRegistry();
        var runId = TraconId.NewId();

        using (var cts = new CancellationTokenSource())
        {
            var registration = registry.Register(runId, runId, "test", cts);
            registration.Dispose();
        }

        registry.ActiveCount.ShouldBe(0);
    }

    [Fact]
    public void Double_release_does_not_throw_and_does_not_corrupt_ActiveCount()
    {
        var registry = new RunCancellationRegistry();
        var runId = TraconId.NewId();

        using var cts = new CancellationTokenSource();
        var registration = registry.Register(runId, runId, "test", cts);

        registration.Dispose();
        registration.Dispose();

        registry.ActiveCount.ShouldBe(0);
    }

    [Fact]
    public void TryCancel_returns_false_for_a_run_that_does_not_exist()
    {
        var registry = new RunCancellationRegistry();

        registry.TryCancel(TraconId.NewId(), "test").ShouldBeFalse();
    }

    [Fact]
    public void A_registered_run_is_canceled_via_TryCancel()
    {
        var registry = new RunCancellationRegistry();
        var runId = TraconId.NewId();

        using var cts = new CancellationTokenSource();
        using var registration = registry.Register(runId, runId, "test", cts);

        registry.TryCancel(runId, "test").ShouldBeTrue();
        cts.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public void Another_tenants_TryCancel_request_is_rejected_and_the_source_is_not_canceled()
    {
        var registry = new RunCancellationRegistry();
        var runId = TraconId.NewId();

        using var cts = new CancellationTokenSource();
        using var registration = registry.Register(runId, runId, "tenant-a", cts);

        registry.TryCancel(runId, "tenant-b").ShouldBeFalse();
        cts.IsCancellationRequested.ShouldBeFalse();
    }

    [Fact]
    public void A_run_that_finishes_while_a_cancel_request_is_in_flight_is_reported_as_not_cancelled()
    {
        // The run's `using` order is registration first, source second,
        // so a TryCancel that already holds the entry can reach a source that the
        // finished run has disposed. Disposing the source while it is still
        // registered is that interleaving, without the timing.
        var registry = new RunCancellationRegistry();
        var runId = TraconId.NewId();

        var cts = new CancellationTokenSource();
        using var registration = registry.Register(runId, runId, "test", cts);
        cts.Dispose();

        registry.TryCancel(runId, "test").ShouldBeFalse();
    }

    [Fact]
    public void A_child_that_already_finished_does_not_stop_the_root_cancel_from_reaching_its_siblings()
    {
        var registry = new RunCancellationRegistry();
        var rootId = TraconId.NewId();
        var finishedChildId = TraconId.NewId();
        var runningChildId = TraconId.NewId();

        using var root = new CancellationTokenSource();
        using var runningChild = new CancellationTokenSource();
        var finishedChild = new CancellationTokenSource();

        using var rootRegistration = registry.Register(rootId, rootId, "test", root);
        using var finishedRegistration = registry.Register(finishedChildId, rootId, "test", finishedChild);
        using var runningRegistration = registry.Register(runningChildId, rootId, "test", runningChild);
        finishedChild.Dispose();

        registry.TryCancel(rootId, "test").ShouldBeTrue();

        root.IsCancellationRequested.ShouldBeTrue();
        runningChild.IsCancellationRequested.ShouldBeTrue();
    }

    /// <summary>
    /// BL-028 / an earlier change: measures that <see cref="IRunCancellationRegistry.TryCancel"/>'s
    /// guarantee is cooperative, not forced. The interface's own XML doc now states this;
    /// this test proves the DOCUMENTED limit, not just the documented behavior — a "run
    /// body" that never reads its token must keep running (and keep "spending", the same
    /// way a real model-provider call would) after a successful <c>TryCancel</c>, exactly
    /// as a consumer reading only the interface's summary must be able to expect.
    /// </summary>
    [Fact]
    public async Task TryCancel_does_not_stop_a_run_body_that_never_reads_its_token()
    {
        var registry = new RunCancellationRegistry();
        var runId = TraconId.NewId();

        using var cts = new CancellationTokenSource();
        using var registration = registry.Register(runId, runId, "test", cts);

        var spendCount = 0;
        using var stop = new CancellationTokenSource();

        // The "run body": simulates work that ignores the cancellation token entirely
        // (the shape a naive or third-party tool call takes) and keeps "spending" until
        // it is told to stop through an UNRELATED channel.
        var runBody = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                Interlocked.Increment(ref spendCount);
                await Task.Delay(5, CancellationToken.None).ConfigureAwait(false); // delay: simulated
            }
        });

        // Let the body actually start spending before cancelling it.
        await WaitUntil.TrueAsync(() => Volatile.Read(ref spendCount) > 0);

        registry.TryCancel(runId, "test").ShouldBeTrue();
        cts.IsCancellationRequested.ShouldBeTrue("TryCancel must still signal the token — the limit is about the BODY, not the signal.");

        var spendAtCancellation = Volatile.Read(ref spendCount);

        // The body keeps running (and "spending") after the signal, because it never
        // reads the token — this is the guarantee limit the interface documents.
        await WaitUntil.TrueAsync(() => Volatile.Read(ref spendCount) > spendAtCancellation);

        stop.Cancel();
        await runBody;
    }
}
