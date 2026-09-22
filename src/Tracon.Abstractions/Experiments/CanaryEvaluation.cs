namespace Tracon;

/// <summary>
/// Represents the result of evaluating a canary policy against the current results
/// of the canary and control variants.
/// </summary>
/// <remarks>
/// This type is <strong>not persisted</strong>. Each
/// <c>GET /api/experiments/{name}/canary</c> call evaluates the policy again
/// with current <see cref="ExperimentVariantResult"/> data. Canary run
/// results already accumulate from the start of the experiment; an evaluation
/// history table would store the same information twice.
/// </remarks>
public sealed record CanaryEvaluation
{
    /// <summary>Gets the evaluation outcome.</summary>
    public required CanaryDecisionKind Decision { get; init; }

    /// <summary>Gets the human-readable reason for the decision.</summary>
    public required string Reason { get; init; }

    /// <summary>Gets the current canary variant results. Returns <see langword="null"/> when no runs exist.</summary>
    public ExperimentVariantResult? Canary { get; init; }

    /// <summary>Gets the current control variant results. Returns <see langword="null"/> when no runs exist.</summary>
    public ExperimentVariantResult? Control { get; init; }

    /// <summary>Gets the UTC time when the evaluation occurred.</summary>
    public required DateTimeOffset EvaluatedAt { get; init; }
}
