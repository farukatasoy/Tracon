using System.ComponentModel;

namespace Tracon;

/// <remarks>
/// This type is <strong>not persisted</strong>. Each
/// <c>GET /api/experiments/{name}/canary</c> call evaluates the policy again
/// with current <see cref="ExperimentVariantResult"/> data. Canary run
/// results already accumulate from the start of the experiment; an evaluation
/// history table would store the same information twice.
/// </remarks>
[Description(
    "Represents the result of evaluating a canary policy against the current results\n" +
    "of the canary and control variants.")]
internal sealed record CanaryEvaluation
{
    [Description("Gets the evaluation outcome.")]
    public required CanaryDecisionKind Decision { get; init; }

    [Description("Gets the human-readable reason for the decision.")]
    public required string Reason { get; init; }

    [Description("Gets the current canary variant results. Returns `null` when no runs exist.")]
    public ExperimentVariantResult? Canary { get; init; }

    [Description("Gets the current control variant results. Returns `null` when no runs exist.")]
    public ExperimentVariantResult? Control { get; init; }

    [Description("Gets the UTC time when the evaluation occurred.")]
    public required DateTimeOffset EvaluatedAt { get; init; }
}
