namespace Tracon;

/// <summary>Carrier for adding a single <see cref="EvalCase"/>.</summary>
public sealed record EvalCaseDraft
{
    /// <summary>The query text sent to the agent.</summary>
    public required string Query { get; init; }

    /// <summary>The expected output.</summary>
    public string? ExpectedOutput { get; init; }

    /// <summary>The expected tool names.</summary>
    public IReadOnlyList<string> ExpectedTools { get; init; } = [];

    /// <summary>Text given to the model as extra context.</summary>
    public string? Context { get; init; }

    /// <summary>The run the case was generated from. <see langword="null"/> for hand-added cases.</summary>
    public Guid? SourceRunId { get; init; }

    /// <summary>The reason for promotion. <see langword="null"/> for hand-added cases.</summary>
    public EvalCaseSource? SourceKind { get; init; }
}
