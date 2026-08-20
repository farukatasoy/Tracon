using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>HTTP response for a run's recorded input.</summary>
public sealed record RunInputResponse
{
    /// <summary>Run identifier.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Creation time of the record (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Input messages, with their polymorphic content, exactly as recorded.
    /// </summary>
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
}

/// <summary>Result of a replay.</summary>
public sealed record RunReplayResponse
{
    /// <summary>Identifier of the newly started run.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Identifier of the source run. Same as <c>runs.replay_of_run_id</c>.</summary>
    public required Guid SourceRunId { get; init; }

    /// <summary>Tool mode applied.</summary>
    public required ReplayToolMode ToolMode { get; init; }

    /// <summary>Definition version used. <see langword="null"/> for a code agent.</summary>
    public int? AgentVersion { get; init; }

    /// <summary>Model used.</summary>
    public string? ModelId { get; init; }

    /// <summary>Text produced by the model.</summary>
    public string? Output { get; init; }

    /// <summary>Address of the endpoint that places the two runs side by side.</summary>
    public required string CompareLocation { get; init; }
}

/// <summary>Side-by-side summary of two runs.</summary>
/// <remarks>
/// The diff is <strong>not computed on the server</strong>; the endpoint
/// only returns the two summaries and the UI shows the comparison. The definition
/// definition version diff follows the same pattern and the UI already has a
/// diff component; a second computation would mean maintenance in two places.
/// </remarks>
public sealed record RunComparisonResponse
{
    /// <summary>The left-hand run.</summary>
    public required RunComparisonSide Left { get; init; }

    /// <summary>The right-hand run.</summary>
    public required RunComparisonSide Right { get; init; }
}

/// <summary>One side of the comparison.</summary>
public sealed record RunComparisonSide
{
    /// <summary>Run identifier.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Agent name.</summary>
    public required string AgentName { get; init; }

    /// <summary>Definition version. <see langword="null"/> if unknown.</summary>
    public int? AgentVersion { get; init; }

    /// <summary>Model used.</summary>
    public string? ModelId { get; init; }

    /// <summary>Final status.</summary>
    public required RunStatus Status { get; init; }

    /// <summary>Duration (milliseconds). <see langword="null"/> if the run has not finished.</summary>
    public long? DurationMs { get; init; }

    /// <summary>Token usage.</summary>
    public RunUsage? Usage { get; init; }

    /// <summary>Cost.</summary>
    public RunCost? Cost { get; init; }

    /// <summary>Number of tool calls.</summary>
    public required int ToolCallCount { get; init; }

    /// <summary>Error class. <see langword="null"/> for a successful run.</summary>
    public RunErrorClass? ErrorClass { get; init; }

    /// <summary>Error message. <see langword="null"/> for a successful run.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Source of this run, if it is a replay.</summary>
    public Guid? ReplayOfRunId { get; init; }

    /// <summary>
    /// Text produced by the model.
    /// </summary>
    /// <remarks>
    /// The non-streaming path writes <c>MessageCompleted</c>, the streaming
    /// path produces only <c>MessageDelta</c>. The two are NOT SUMMED:
    /// if <c>MessageCompleted</c> is present it is used, otherwise the chunks
    /// are concatenated — otherwise the text would be counted twice on the
    /// non-streaming path.
    /// </remarks>
    public string? Output { get; init; }

    /// <summary>Scores written against this run.</summary>
    public IReadOnlyList<RunScore> Scores { get; init; } = [];
}
