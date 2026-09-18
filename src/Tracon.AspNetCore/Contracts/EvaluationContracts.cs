using System.Text.Json;

namespace Tracon;

/// <summary>Request to create/update an eval suite.</summary>
/// <remarks>
/// The name comes from the <em>path</em>, not the body — same rationale as <see
/// cref="JobScheduleSaveRequest"/>.
/// </remarks>
public sealed record EvalSuiteSaveRequest
{
    /// <summary>Short description.</summary>
    public string? Description { get; init; }

    /// <summary>Name of the agent this suite measures.</summary>
    public required string AgentName { get; init; }

    /// <summary>Check definitions. See <c>EvalSuite.Checks</c>.</summary>
    public JsonElement Checks { get; init; }
}

/// <summary>Input shape of an eval case (in a request).</summary>
public sealed record EvalCaseInput
{
    /// <summary>
    /// The identifier of the case this entry is, or <see langword="null"/> to
    /// create a new one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A case keeps this identifier for its whole life, and the run-to-run
    /// diff behind <c>--baseline</c> is matched on it. Send back the
    /// identifier <c>GET /api/evals/{name}/cases</c> gave you for every case
    /// you are keeping; a case that arrives without one is a new case and gets
    /// a new identifier.
    /// </para>
    /// <para>
    /// Editing a case's text while keeping its identifier is the normal way to
    /// revise it — the diff then reads the revision as the same case, not as
    /// one case removed and another added.
    /// </para>
    /// </remarks>
    public Guid? Id { get; init; }

    /// <summary>Query text to send to the agent.</summary>
    public required string Query { get; init; }

    /// <summary>Expected output.</summary>
    public string? ExpectedOutput { get; init; }

    /// <summary>Tool names looked for by the <c>toolCalled</c> check.</summary>
    public IReadOnlyList<string> ExpectedTools { get; init; } = [];

    /// <summary>Text to give the model as extra context.</summary>
    public string? Context { get; init; }

    /// <summary>
    /// Values for the target agent's parameter schema, or <see langword="null"/>
    /// for an agent that declares none.
    /// </summary>
    /// <remarks>
    /// These are part of the case, not of the request: an agent whose
    /// instructions reference <c>{{name}}</c> placeholders cannot be evaluated
    /// without a value for each one, and a case that arrives without them
    /// fails the same missing-parameter check a normal run applies. Send them
    /// back with every case you are keeping, exactly as
    /// <c>GET /api/evals/{name}/cases</c> returned them.
    /// </remarks>
    public IReadOnlyDictionary<string, string>? Parameters { get; init; }
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

/// <summary>Request to promote a run to a case.</summary>
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
