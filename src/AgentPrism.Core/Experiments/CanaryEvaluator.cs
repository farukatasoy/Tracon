using System.Globalization;

namespace AgentPrism;

/// <summary>
/// Pure decision logic that evaluates a <see cref="CanaryPolicy"/> against the current
/// results of the canary and control arms.
/// </summary>
/// <remarks>
/// <para>
/// It does not call a database or model. <see cref="Evaluate"/> operates only on
/// already collected <see cref="ExperimentVariantResult"/> data. Data collection is
/// the responsibility of <c>CanaryEvaluationService</c>. This separation has the same
/// rationale as <c>ExperimentAssignmentResolver.SelectVariant</c>: tests run without a database.
/// </para>
/// <para>
/// This class is <c>public</c> because <c>GET /api/experiments/{name}/canary</c> in
/// <c>AgentPrism.AspNetCore</c> calculates the "latest evaluation" live with this
/// method. This has the same rationale as public <see cref="ExperimentAssignmentResolver"/>.
/// </para>
/// </remarks>
public static class CanaryEvaluator
{
    /// <summary>
    /// Evaluates the canary policy against the current results of the canary and control arms.
    /// </summary>
    /// <param name="policy">The canary policy.</param>
    /// <param name="results">The current results for all experiment arms.</param>
    /// <param name="now">The evaluation time in UTC.</param>
    /// <returns>The evaluation result.</returns>
    public static CanaryEvaluation Evaluate(CanaryPolicy policy, IReadOnlyList<ExperimentVariantResult> results, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(results);

        var canary = results.FirstOrDefault(result => string.Equals(result.Variant, policy.CanaryVariant, StringComparison.Ordinal));
        var control = results.FirstOrDefault(result => !string.Equals(result.Variant, policy.CanaryVariant, StringComparison.Ordinal));

        var canarySettled = SettledRuns(canary);
        var controlSettled = SettledRuns(control);

        if (canary is null || control is null || canarySettled < policy.MinSampleSize || controlSettled < policy.MinSampleSize)
        {
            return Build(
                CanaryDecisionKind.InsufficientData,
                $"The minimum settled run count ({policy.MinSampleSize}) was not reached: " +
                $"canary {canarySettled}, control {controlSettled}.",
                canary,
                control,
                now);
        }

        if (policy.MaxErrorRateDelta is { } maxDelta && canary.ErrorRate is { } canaryRate && control.ErrorRate is { } controlRate)
        {
            var delta = canaryRate - controlRate;

            if (delta > maxDelta)
            {
                return Build(
                    CanaryDecisionKind.RollBack,
                    $"The canary error rate ({canaryRate.ToString("P1", CultureInfo.InvariantCulture)}) exceeds the control rate ({controlRate.ToString("P1", CultureInfo.InvariantCulture)}) by more than the {maxDelta.ToString("P1", CultureInfo.InvariantCulture)} threshold.",
                    canary,
                    control,
                    now);
            }
        }

        if (policy.MinScore is { } minScore && canary.AverageScore is { } averageScore && averageScore < minScore)
        {
            return Build(
                CanaryDecisionKind.RollBack,
                $"The canary average score ({averageScore.ToString("F1", CultureInfo.InvariantCulture)}) is below the {minScore} threshold.",
                canary,
                control,
                now);
        }

        return Build(CanaryDecisionKind.Healthy, "The canary is not worse than control by the threshold.", canary, control, now);
    }

    private static long SettledRuns(ExperimentVariantResult? result)
        => result is null ? 0 : result.CompletedRuns + result.FailedRuns + result.CanceledRuns;

    private static CanaryEvaluation Build(
        CanaryDecisionKind decision,
        string reason,
        ExperimentVariantResult? canary,
        ExperimentVariantResult? control,
        DateTimeOffset now)
        => new()
        {
            Decision = decision,
            Reason = reason,
            Canary = canary,
            Control = control,
            EvaluatedAt = now,
        };
}
