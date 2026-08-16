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
}
