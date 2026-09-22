namespace Tracon;

/// <summary>Request to create/update an experiment. The name comes from the path.</summary>
public sealed record ExperimentSaveRequest
{
    /// <summary>Name of the agent whose traffic is split.</summary>
    public required string AgentName { get; init; }

    /// <summary>The experiment's arms. Weights must sum to 100.</summary>
    public required IReadOnlyList<ExperimentVariant> Variants { get; init; }
}

/// <summary>Per-variant results view of an experiment.</summary>
public sealed record ExperimentResultsResponse
{
    /// <summary>The experiment itself.</summary>
    public required Experiment Experiment { get; init; }

    /// <summary>Per-arm results.</summary>
    public required IReadOnlyList<ExperimentVariantResult> Results { get; init; }
}

/// <summary>
/// Response for <c>GET /api/experiments/{name}/canary</c> — the rule AND its
/// current evaluation together.
/// </summary>
/// <remarks>
/// <see cref="Evaluation"/> is NOT PERSISTENT: it is computed LIVE on every
/// call (see the <see cref="CanaryEvaluation"/> class documentation).
/// </remarks>
public sealed record ExperimentCanaryResponse
{
    /// <summary>The defined canary rule. <see langword="null"/> if none has been defined.</summary>
    public CanaryPolicy? Policy { get; init; }

    /// <summary>Current evaluation of the rule. <see langword="null"/> if <c>Policy</c> is <see langword="null"/>.</summary>
    public CanaryEvaluation? Evaluation { get; init; }
}
