namespace Tracon;

/// <summary>Carrier for adding a single <see cref="EvalCase"/>.</summary>
public sealed record EvalCaseDraft
{
    /// <summary>The query text sent to the agent.</summary>
    public required string Query { get; init; }

    /// <summary>The expected output.</summary>
    public string? ExpectedOutput { get; init; }

    /// <summary>The expected tool calls. See <see cref="EvalCase.ExpectedToolCalls"/>.</summary>
    public IReadOnlyList<EvalExpectedToolCall> ExpectedToolCalls { get; init; } = [];

    /// <summary>Text given to the model as extra context.</summary>
    /// <remarks>
    /// The agent receives it through the document channel, ahead of
    /// <see cref="Query"/>; see <see cref="EvalCase.Context"/>. A promoted case
    /// stores the source run's document channel blocks here verbatim.
    /// </remarks>
    public string? Context { get; init; }

    /// <summary>The run the case was generated from. <see langword="null"/> for hand-added cases.</summary>
    public Guid? SourceRunId { get; init; }

    /// <summary>The reason for promotion. <see langword="null"/> for hand-added cases.</summary>
    public EvalCaseSource? SourceKind { get; init; }

    /// <summary>
    /// Values for the target agent's <see cref="AgentDefinition.Parameters"/>
    /// schema. <see langword="null"/> for an agent that declares no parameters.
    /// See <see cref="EvalCase.Parameters"/>.
    /// </summary>
    /// <remarks>
    /// A promoted case copies the values the source run recorded
    /// (<see cref="RunInputRecord.Parameters"/>, defaults included), so the case
    /// is bound with what the production run was told. An
    /// <see cref="IEvalStore"/> writes them like the case's other fields; one
    /// that drops them leaves a promoted case of a parameterized agent failing
    /// on a missing value, or running on a default the run never used.
    /// </remarks>
    public IReadOnlyDictionary<string, string>? Parameters { get; init; }
}
