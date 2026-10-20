using System.ComponentModel;
using System.Text.Json;

namespace Tracon;

/// <remarks>
/// The name comes from the <em>path</em>, not the body — same rationale as <see
/// cref="JobScheduleSaveRequest"/>.
/// </remarks>
[Description("Request to create/update an eval suite.")]
internal sealed record EvalSuiteSaveRequest
{
    [Description("Short description.")]
    public string? Description { get; init; }

    [Description("Name of the agent this suite measures.")]
    public required string AgentName { get; init; }

    [Description("Check definitions. See `EvalSuite.Checks`.")]
    public JsonElement Checks { get; init; }
}

[Description("Input shape of an eval case (in a request).")]
internal sealed record EvalCaseInput
{
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
    [Description(
        "The identifier of the case this entry is, or `null` to\n" +
        "create a new one.")]
    public Guid? Id { get; init; }

    [Description("Query text to send to the agent.")]
    public required string Query { get; init; }

    [Description("Expected output.")]
    public string? ExpectedOutput { get; init; }

    /// <remarks>
    /// Read by the <c>toolCallArgsMatch</c> check. Replaces <c>expectedTools</c>,
    /// which held tool names only and was not read by any check; the API refuses
    /// a body that still carries it.
    /// </remarks>
    [Description(
        "Tool calls the agent is expected to make, read by the `toolCallArgsMatch`\n" +
        "check. Each entry is `{\"name\": ..., \"arguments\": {...}}`; without\n" +
        "`arguments` the entry checks only that the tool was called. Listed\n" +
        "arguments must all be carried by one call to that tool, which may carry\n" +
        "others, and values compare by JSON meaning.")]
    public IReadOnlyList<EvalExpectedToolCall> ExpectedToolCalls { get; init; } = [];

    /// <remarks>
    /// The agent receives it through the document channel, ahead of the query,
    /// and the evaluator receives it as the case's context. Plain text goes as
    /// one document named <c>context</c>. A promoted case stores the run's
    /// documents here as document channel blocks, and each block goes back as
    /// its own document; send the text back unchanged to keep that. See
    /// <c>EvalCase.Context</c>.
    /// </remarks>
    [Description("Text to give the model as extra context.")]
    public string? Context { get; init; }

    /// <remarks>
    /// These are part of the case, not of the request: an agent whose
    /// instructions reference <c>{{name}}</c> placeholders cannot be evaluated
    /// without a value for each one, and a case that arrives without them
    /// fails the same missing-parameter check a normal run applies. Send them
    /// back with every case you are keeping, exactly as
    /// <c>GET /api/evals/{name}/cases</c> returned them.
    /// </remarks>
    [Description(
        "Values for the target agent's parameter schema, or `null`\n" +
        "for an agent that declares none.")]
    public IReadOnlyDictionary<string, string>? Parameters { get; init; }
}

[Description("Request to trigger an eval run immediately.")]
internal sealed record EvalRunTriggerRequest
{
    [Description(
        "Model identifier to record for this run. If not given, the model of\n" +
        "the definition version the run measures is used: the pinned version\n" +
        "when one is given, otherwise the current one.")]
    public string? ModelId { get; init; }

    [Description(
        "Number of times to repeat each case to measure its stability. If not\n" +
        "given, 1.")]
    public int? NumRepetitions { get; init; }

    [Description(
        "Definition version to measure for this run. If not given, the agent's\n" +
        "current version is used. Rejected with 400 for code-sourced agents (no\n" +
        "version history).")]
    public int? AgentVersion { get; init; }
}

[Description("Request to promote a run to a case.")]
internal sealed record EvalCasePromotionRequest
{
    [Description(
        "Overrides the promotion reason. If not given, it is derived\n" +
        "automatically from the run's status and score.")]
    public EvalCaseSource? SourceKind { get; init; }
}

[Description("Detailed view of a single eval run: summary and case results together.")]
internal sealed record EvalRunDetailResponse
{
    [Description("Run summary.")]
    public required EvalRun Run { get; init; }

    [Description("Per-case results.")]
    public required IReadOnlyList<EvalCaseResult> Results { get; init; }
}
