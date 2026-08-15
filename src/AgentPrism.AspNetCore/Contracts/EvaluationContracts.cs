using System.Text.Json;

namespace AgentPrism;

/// <summary>Request to create/update an eval suite.</summary>
/// <remarks>The name comes from the <em>path</em>, not the body — same rationale as <see cref="JobScheduleSaveRequest"/>.</remarks>
public sealed record EvalSuiteSaveRequest
{
    /// <summary>Short description.</summary>
    public string? Description { get; init; }

    /// <summary>Name of the agent this suite measures.</summary>
    public required string AgentName { get; init; }

    /// <summary>Check definitions. See <see cref="EvalSuite.Checks"/>.</summary>
    public JsonElement Checks { get; init; }
}

/// <summary>Input shape of an eval case (in a request).</summary>
public sealed record EvalCaseInput
{
    /// <summary>Query text to send to the agent.</summary>
    public required string Query { get; init; }

    /// <summary>Expected output.</summary>
    public string? ExpectedOutput { get; init; }

    /// <summary>Tool names looked for by the <c>toolCalled</c> check.</summary>
    public IReadOnlyList<string> ExpectedTools { get; init; } = [];

    /// <summary>Text to give the model as extra context.</summary>
    public string? Context { get; init; }
}

/// <summary>Request to trigger an eval run immediately.</summary>
public sealed record EvalRunTriggerRequest
{
    /// <summary>
    /// Model identifier to record for this run. If not given, the model in
    /// the agent's current definition is used.
    /// </summary>
    public string? ModelId { get; init; }

    /// <summary>
    /// Number of times to repeat each case to measure its stability. If not
    /// given, 1.
    /// </summary>
    public int? NumRepetitions { get; init; }

    /// <summary>
    /// Definition version to measure for this run. If not given, the agent's
    /// current version is used. Rejected with 400 for code-sourced agents (no
    /// version history).
    /// </summary>
    public int? AgentVersion { get; init; }
}

/// <summary>Request to promote a run to a case (Phase 45, F-53).</summary>
public sealed record EvalCasePromotionRequest
{
    /// <summary>
    /// Overrides the promotion reason. If not given, it is derived
    /// automatically from the run's status and score.
    /// </summary>
    public EvalCaseSource? SourceKind { get; init; }
}

/// <summary>Detailed view of a single eval run: summary and case results together.</summary>
public sealed record EvalRunDetailResponse
{
    /// <summary>Run summary.</summary>
    public required EvalRun Run { get; init; }

    /// <summary>Per-case results.</summary>
    public required IReadOnlyList<EvalCaseResult> Results { get; init; }
}
