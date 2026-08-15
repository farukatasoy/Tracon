namespace AgentPrism;

/// <summary>A single test case inside an <see cref="EvalSuite"/>.</summary>
public sealed record EvalCase
{
    /// <summary>The case identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>The identifier of the suite this case belongs to.</summary>
    public required Guid SuiteId { get; init; }

    /// <summary>The sequence number within the suite (starts at 0).</summary>
    public required int Seq { get; init; }

    /// <summary>The query text sent to the agent.</summary>
    public required string Query { get; init; }

    /// <summary>
    /// The expected output. Referenced by the <c>containsExpected</c> check.
    /// </summary>
    public string? ExpectedOutput { get; init; }

    /// <summary>
    /// The tool names looked up by the <c>toolCalled</c> check. An empty list
    /// does not mean the check accepts any tool call — the check is still
    /// defined separately in the suite's <c>checks</c> field.
    /// </summary>
    public IReadOnlyList<string> ExpectedTools { get; init; } = [];

    /// <summary>Text given to the model as extra context.</summary>
    public string? Context { get; init; }

    /// <summary>The run the case was generated from. <see langword="null"/> when hand-written.</summary>
    public Guid? SourceRunId { get; init; }

    /// <summary>The reason for promotion. <see langword="null"/> when hand-written.</summary>
    public EvalCaseSource? SourceKind { get; init; }

    /// <summary>The promotion time. <see langword="null"/> when hand-written.</summary>
    public DateTimeOffset? PromotedAt { get; init; }
}
