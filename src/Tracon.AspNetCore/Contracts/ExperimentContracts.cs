using System.ComponentModel;

namespace Tracon;

[Description("Request to create/update an experiment. The name comes from the path.")]
internal sealed record ExperimentSaveRequest
{
    [Description("Name of the agent whose traffic is split.")]
    public required string AgentName { get; init; }

    [Description("The experiment's arms. Weights must sum to 100.")]
    public required IReadOnlyList<ExperimentVariant> Variants { get; init; }
}

[Description("Per-variant results view of an experiment.")]
internal sealed record ExperimentResultsResponse
{
    [Description("The experiment itself.")]
    public required Experiment Experiment { get; init; }

    [Description("Per-arm results.")]
    public required IReadOnlyList<ExperimentVariantResult> Results { get; init; }
}

/// <remarks>
/// <see cref="Evaluation"/> is NOT PERSISTENT: it is computed LIVE on every
/// call (see the <see cref="CanaryEvaluation"/> class documentation).
/// </remarks>
[Description(
    "Response for `GET /api/experiments/{name}/canary` — the rule AND its\n" +
    "current evaluation together.")]
internal sealed record ExperimentCanaryResponse
{
    [Description("The defined canary rule. `null` if none has been defined.")]
    public CanaryPolicy? Policy { get; init; }

    [Description("Current evaluation of the rule. `null` if `Policy` is `null`.")]
    public CanaryEvaluation? Evaluation { get; init; }
}
