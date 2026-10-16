using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace Tracon;

[Description("HTTP response for a run's recorded input.")]
internal sealed record RunInputResponse
{
    [Description("Run identifier.")]
    public required Guid RunId { get; init; }

    [Description("Creation time of the record (UTC).")]
    public required DateTimeOffset CreatedAt { get; init; }

    [Description("Input messages, with their polymorphic content, exactly as recorded.")]
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
}

[Description("Result of a replay.")]
internal sealed record RunReplayResponse
{
    [Description("Identifier of the newly started run.")]
    public required Guid RunId { get; init; }

    [Description("Identifier of the source run. Same as `runs.replay_of_run_id`.")]
    public required Guid SourceRunId { get; init; }

    [Description("Tool mode applied.")]
    public required ReplayToolMode ToolMode { get; init; }

    [Description("Definition version used. `null` for a code agent.")]
    public int? AgentVersion { get; init; }

    [Description("Model used.")]
    public string? ModelId { get; init; }

    [Description("Text produced by the model.")]
    public string? Output { get; init; }

    [Description("Address of the endpoint that places the two runs side by side.")]
    public required string CompareLocation { get; init; }
}

/// <remarks>
/// The diff is <strong>not computed on the server</strong>; the endpoint
/// only returns the two summaries and the UI shows the comparison. The definition
/// definition version diff follows the same pattern and the UI already has a
/// diff component; a second computation would mean maintenance in two places.
/// </remarks>
[Description("Side-by-side summary of two runs.")]
internal sealed record RunComparisonResponse
{
    [Description("The left-hand run.")]
    public required RunComparisonSide Left { get; init; }

    [Description("The right-hand run.")]
    public required RunComparisonSide Right { get; init; }
}

[Description("One side of the comparison.")]
internal sealed record RunComparisonSide
{
    [Description("Run identifier.")]
    public required Guid RunId { get; init; }

    [Description("Agent name.")]
    public required string AgentName { get; init; }

    [Description("Definition version. `null` if unknown.")]
    public int? AgentVersion { get; init; }

    [Description("Model used.")]
    public string? ModelId { get; init; }

    [Description("Final status.")]
    public required RunStatus Status { get; init; }

    [Description("Duration (milliseconds). `null` if the run has not finished.")]
    public long? DurationMs { get; init; }

    [Description("Token usage.")]
    public RunUsage? Usage { get; init; }

    [Description("Cost.")]
    public RunCost? Cost { get; init; }

    [Description("Number of tool calls.")]
    public required int ToolCallCount { get; init; }

    [Description("Error class. `null` for a successful run.")]
    public RunErrorClass? ErrorClass { get; init; }

    [Description("Error message. `null` for a successful run.")]
    public string? ErrorMessage { get; init; }

    [Description("Source of this run, if it is a replay.")]
    public Guid? ReplayOfRunId { get; init; }

    /// <remarks>
    /// The non-streaming path writes <c>MessageCompleted</c>, the streaming
    /// path produces only <c>MessageDelta</c>. The two are NOT SUMMED:
    /// if <c>MessageCompleted</c> is present it is used, otherwise the chunks
    /// are concatenated — otherwise the text would be counted twice on the
    /// non-streaming path.
    /// </remarks>
    [Description("Text produced by the model.")]
    public string? Output { get; init; }

    [Description("Scores written against this run.")]
    public IReadOnlyList<RunScore> Scores { get; init; } = [];
}
