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
    /// The tool names this case is expected to exercise, recorded for reference.
    /// </summary>
    /// <remarks>
    /// <strong>Not read by any check.</strong> The <c>toolCalled</c> check takes
    /// its tool names from its own <c>tools</c> field in the suite's
    /// <c>checks</c> definition, so filling this in does not assert anything and
    /// leaving it empty does not weaken a check. It exists so a case can carry
    /// what it was written to cover.
    /// </remarks>
    public IReadOnlyList<string> ExpectedTools { get; init; } = [];

    /// <summary>Text given to the model as extra context.</summary>
    public string? Context { get; init; }

    /// <summary>The run the case was generated from. <see langword="null"/> when hand-written.</summary>
    public Guid? SourceRunId { get; init; }

    /// <summary>The reason for promotion. <see langword="null"/> when hand-written.</summary>
    public EvalCaseSource? SourceKind { get; init; }

    /// <summary>The promotion time. <see langword="null"/> when hand-written.</summary>
    public DateTimeOffset? PromotedAt { get; init; }

    /// <summary>
    /// Values for the target agent's <see cref="AgentDefinition.Parameters"/>
    /// schema. <see langword="null"/> for an agent that declares no parameters.
    /// </summary>
    /// <remarks>
    /// Evaluating a parameterized agent without this field would run every
    /// case against unresolved <c>{{name}}</c> placeholders left literally in
    /// the instructions text - the same missing-parameter check that
    /// <c>POST /api/agents/{name}/run</c> applies also runs here, and a case
    /// missing a required value fails outright rather than running with a
    /// broken prompt.
    /// </remarks>
    public IReadOnlyDictionary<string, string>? Parameters { get; init; }
}
