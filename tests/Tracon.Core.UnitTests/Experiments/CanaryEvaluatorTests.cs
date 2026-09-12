namespace Tracon.Core.UnitTests.Experiments;

/// <summary>
/// The <see cref="CanaryEvaluator"/> contract — pure decision logic, no database
/// (Phase 56).
/// </summary>
public sealed class CanaryEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Error_rate_over_threshold_triggers_rollback()
    {
        var policy = Policy(maxErrorRateDelta: 0.1);

        // Canary has 30% errors, control has 5% errors -- the gap (25%) exceeds the threshold (10%).
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
    public void No_decision_is_made_when_MinSampleSize_is_not_reached()
    {
        var policy = Policy(maxErrorRateDelta: 0.01, minSampleSize: 20);

        // The canary has only 5 settled runs -- well below the threshold.
        var results = new[]
        {
            Variant("canary", completed: 0, failed: 5, canceled: 0),
            Variant("control", completed: 25, failed: 0, canceled: 0),
        };

        var evaluation = CanaryEvaluator.Evaluate(policy, results, Now);

        evaluation.Decision.ShouldBe(CanaryDecisionKind.InsufficientData);
    }

    [Fact]
    public void Relative_comparison_does_not_roll_back_when_control_is_also_bad()
    {
        var policy = Policy(maxErrorRateDelta: 0.15);

        // Canary has 50% errors, control has 40% errors -- the gap is only 10%, CLEARLY below the threshold (15%).
        var results = new[]
        {
            Variant("canary", completed: 10, failed: 10, canceled: 0),
            Variant("control", completed: 12, failed: 8, canceled: 0),
        };

        var evaluation = CanaryEvaluator.Evaluate(policy, results, Now);

        evaluation.Decision.ShouldBe(CanaryDecisionKind.Healthy);
    }

    [Fact]
    public void Score_below_threshold_triggers_rollback()
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
    public void Score_threshold_is_skipped_and_counted_healthy_when_score_is_missing()
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
    public void No_decision_is_made_when_the_canary_arm_never_ran()
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
