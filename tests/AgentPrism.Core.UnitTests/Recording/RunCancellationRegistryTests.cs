namespace AgentPrism.Core.UnitTests.Recording;

public sealed class RunCancellationRegistryTests
{
    [Fact]
    public void Kayit_ActiveCount_artirir()
    {
        var registry = new RunCancellationRegistry();
        var runId = AgentPrismId.NewId();

        using var cts = new CancellationTokenSource();
        using var registration = registry.Register(runId, runId, "test", cts);

        registry.ActiveCount.ShouldBe(1);
    }

    [Fact]
    public void Birakma_ActiveCount_sifira_doner()
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
    public void Cift_birakma_hata_vermez_ve_ActiveCount_bozulmaz()
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
    public void Olmayan_calistirma_icin_TryCancel_false_doner()
    {
        var registry = new RunCancellationRegistry();

        registry.TryCancel(AgentPrismId.NewId(), "test").ShouldBeFalse();
    }

    [Fact]
    public void Kayitli_calistirma_TryCancel_ile_iptal_edilir()
    {
        var registry = new RunCancellationRegistry();
        var runId = AgentPrismId.NewId();

        using var cts = new CancellationTokenSource();
        using var registration = registry.Register(runId, runId, "test", cts);

        registry.TryCancel(runId, "test").ShouldBeTrue();
        cts.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public void Baska_kiracinin_TryCancel_istegi_reddedilir_ve_kaynak_iptal_edilmez()
    {
        var registry = new RunCancellationRegistry();
        var runId = AgentPrismId.NewId();

        using var cts = new CancellationTokenSource();
        using var registration = registry.Register(runId, runId, "kiraci-a", cts);

        registry.TryCancel(runId, "kiraci-b").ShouldBeFalse();
        cts.IsCancellationRequested.ShouldBeFalse();
    }
}
