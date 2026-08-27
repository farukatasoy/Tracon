namespace AgentPrism.Core.UnitTests.Recording;

public sealed class RunCancellationRegistryTests
{
    [Fact]
    public void Registering_increases_ActiveCount()
    {
        var registry = new RunCancellationRegistry();
        var runId = AgentPrismId.NewId();

        using var cts = new CancellationTokenSource();
        using var registration = registry.Register(runId, runId, "test", cts);

        registry.ActiveCount.ShouldBe(1);
    }

    [Fact]
    public void Releasing_returns_ActiveCount_to_zero()
    {
        var registry = new RunCancellationRegistry();
        var runId = AgentPrismId.NewId();

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
        var runId = AgentPrismId.NewId();

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

        registry.TryCancel(AgentPrismId.NewId(), "test").ShouldBeFalse();
    }

    [Fact]
    public void A_registered_run_is_canceled_via_TryCancel()
    {
        var registry = new RunCancellationRegistry();
        var runId = AgentPrismId.NewId();

        using var cts = new CancellationTokenSource();
        using var registration = registry.Register(runId, runId, "test", cts);

        registry.TryCancel(runId, "test").ShouldBeTrue();
        cts.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public void Another_tenants_TryCancel_request_is_rejected_and_the_source_is_not_canceled()
    {
        var registry = new RunCancellationRegistry();
        var runId = AgentPrismId.NewId();

        using var cts = new CancellationTokenSource();
        using var registration = registry.Register(runId, runId, "tenant-a", cts);

        registry.TryCancel(runId, "tenant-b").ShouldBeFalse();
        cts.IsCancellationRequested.ShouldBeFalse();
    }

    /// <summary>
    /// BL-028 / Phase 121: measures that <see cref="IRunCancellationRegistry.TryCancel"/>'s
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
        var runId = AgentPrismId.NewId();

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
                await Task.Delay(5, CancellationToken.None).ConfigureAwait(false);
            }
        });

        // Let the body actually start spending before cancelling it.
        await WaitUntilAsync(() => Volatile.Read(ref spendCount) > 0);

        registry.TryCancel(runId, "test").ShouldBeTrue();
        cts.IsCancellationRequested.ShouldBeTrue("TryCancel must still signal the token — the limit is about the BODY, not the signal.");

        var spendAtCancellation = Volatile.Read(ref spendCount);

        // The body keeps running (and "spending") after the signal, because it never
        // reads the token — this is the guarantee limit the interface documents.
        await WaitUntilAsync(() => Volatile.Read(ref spendCount) > spendAtCancellation);

        stop.Cancel();
        await runBody;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!condition())
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(5, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
