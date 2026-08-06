namespace AgentPrism.Core.UnitTests.Recording;

/// <summary>
/// Agac cascade davranisini <see cref="RunCancellationRegistry"/> seviyesinde
/// dogrular: kok iptali alt calistirmalara yayilir, alt calistirmanin tek
/// basina iptali ne koku ne kardes dallari etkiler.
/// </summary>
public sealed class RunCancellationTreeTests
{
    [Fact]
    public void Kok_iptali_tum_alt_calistirmalari_iptal_eder()
    {
        var registry = new RunCancellationRegistry();
        var rootId = AgentPrismId.NewId();
        var childId1 = AgentPrismId.NewId();
        var childId2 = AgentPrismId.NewId();
        var unrelatedRootId = AgentPrismId.NewId();

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

        // Ayni RootRunId'yi paylasmayan baska bir agac etkilenmez.
        unrelatedCts.IsCancellationRequested.ShouldBeFalse();
    }

    [Fact]
    public void Alt_calistirmanin_tek_basina_iptali_koku_ve_kardes_dali_etkilemez()
    {
        var registry = new RunCancellationRegistry();
        var rootId = AgentPrismId.NewId();
        var childId1 = AgentPrismId.NewId();
        var childId2 = AgentPrismId.NewId();

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
