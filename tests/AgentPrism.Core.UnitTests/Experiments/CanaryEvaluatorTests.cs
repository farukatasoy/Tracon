namespace AgentPrism.Core.UnitTests.Experiments;

/// <summary>
/// <see cref="CanaryEvaluator"/> sozlesmesi — saf karar mantigi, veritabani yok
/// (Faz 56).
/// </summary>
public sealed class CanaryEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Esigi_asan_hata_orani_geri_almaya_karar_verir()
    {
        var policy = Policy(maxErrorRateDelta: 0.1);

        // Kanarya %30 hata, kontrol %5 hata -- fark (%25) esigi (%10) asiyor.
        var results = new[]
        {
            Variant("canary", completed: 14, failed: 6, canceled: 0),
            Variant("control", completed: 19, failed: 1, canceled: 0),
        };

        var evaluation = CanaryEvaluator.Evaluate(policy, results, Now);

        evaluation.Decision.ShouldBe(CanaryDecisionKind.RollBack);
        evaluation.Reason.ShouldContain("error rate");
    }

    [Fact]
    public void MinSampleSizeya_ulasilmadiysa_karar_verilmez()
    {
        var policy = Policy(maxErrorRateDelta: 0.01, minSampleSize: 20);

        // Kanarya yalniz 5 sonuclanmis calistirmaya sahip -- esik cok altinda.
        var results = new[]
        {
            Variant("canary", completed: 0, failed: 5, canceled: 0),
            Variant("control", completed: 25, failed: 0, canceled: 0),
        };

        var evaluation = CanaryEvaluator.Evaluate(policy, results, Now);

        evaluation.Decision.ShouldBe(CanaryDecisionKind.InsufficientData);
    }

    [Fact]
    public void Kontrol_de_kotuyse_gorece_karsilastirma_geri_almaz()
    {
        var policy = Policy(maxErrorRateDelta: 0.15);

        // Kanarya %50 hata, kontrol %40 hata -- fark yalniz %10, esigin (%15) ACIKCA ALTINDA.
        var results = new[]
        {
            Variant("canary", completed: 10, failed: 10, canceled: 0),
            Variant("control", completed: 12, failed: 8, canceled: 0),
        };

        var evaluation = CanaryEvaluator.Evaluate(policy, results, Now);

        evaluation.Decision.ShouldBe(CanaryDecisionKind.Healthy);
    }

    [Fact]
    public void Esigin_altindaki_puan_geri_almaya_karar_verir()
    {
        var policy = Policy(minScore: 70);

        var results = new[]
        {
            Variant("canary", completed: 20, failed: 0, canceled: 0, averageScore: 55.0),
            Variant("control", completed: 20, failed: 0, canceled: 0, averageScore: 90.0),
        };

        var evaluation = CanaryEvaluator.Evaluate(policy, results, Now);

        evaluation.Decision.ShouldBe(CanaryDecisionKind.RollBack);
        evaluation.Reason.ShouldContain("average score");
    }

    [Fact]
    public void Puan_yoksa_puan_esigi_atlanir_ve_saglikli_sayilir()
    {
        var policy = Policy(minScore: 70);

        var results = new[]
        {
            Variant("canary", completed: 20, failed: 0, canceled: 0, averageScore: null),
            Variant("control", completed: 20, failed: 0, canceled: 0, averageScore: null),
        };

        var evaluation = CanaryEvaluator.Evaluate(policy, results, Now);

        evaluation.Decision.ShouldBe(CanaryDecisionKind.Healthy);
    }

    [Fact]
    public void Kanarya_kolu_hic_calismamissa_karar_verilmez()
    {
        var policy = Policy(maxErrorRateDelta: 0.1);

        var results = new[] { Variant("control", completed: 30, failed: 0, canceled: 0) };

        var evaluation = CanaryEvaluator.Evaluate(policy, results, Now);

        evaluation.Decision.ShouldBe(CanaryDecisionKind.InsufficientData);
        evaluation.Canary.ShouldBeNull();
    }

    private static CanaryPolicy Policy(
        double? maxErrorRateDelta = null,
        int? minScore = null,
        int minSampleSize = 20)
        => new()
        {
            CanaryVariant = "canary",
            MaxErrorRateDelta = maxErrorRateDelta,
            MinScore = minScore,
            MinSampleSize = minSampleSize,
        };

    private static ExperimentVariantResult Variant(
        string name,
        long completed,
        long failed,
        long canceled,
        double? averageScore = null)
        => new()
        {
            Variant = name,
            Version = 1,
            TotalRuns = completed + failed + canceled,
            CompletedRuns = completed,
            FailedRuns = failed,
            CanceledRuns = canceled,
            AverageScore = averageScore,
        };
}
