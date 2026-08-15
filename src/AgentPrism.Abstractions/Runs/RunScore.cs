namespace AgentPrism;

/// <summary>
/// A human (or judge) score for a run or for a single message within a run.
/// </summary>
/// <remarks>
/// <para>
/// If <see cref="MessageId"/> is empty, the score belongs to
/// <strong>the whole run</strong>; if set, it belongs to a single message.
/// </para>
/// <para>
/// An author (<see cref="Author"/>) scores the same target (a run or a
/// message) once — a second write updates the existing row,
/// <see cref="IRunScoreStore.UpsertAsync"/>. When <see cref="Author"/> is
/// empty (an identity-less setup), this rule does not apply; every call
/// opens a new row.
/// </para>
/// </remarks>
public sealed record RunScore
{
    /// <summary>The score's identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>The tenant.</summary>
    public required string TenantId { get; init; }

    /// <summary>The identifier of the run being scored.</summary>
    public required Guid RunId { get; init; }

    /// <summary>The identifier of the message being scored. If empty, the score belongs to the whole run.</summary>
    public string? MessageId { get; init; }

    /// <summary>The shape of <see cref="Value"/>.</summary>
    public required RunScoreKind Kind { get; init; }

    /// <summary>The score value. 0/1 for <see cref="RunScoreKind.Binary"/>, 1..5 for <see cref="RunScoreKind.Stars"/>.</summary>
    public required int Value { get; init; }

    /// <summary>A free-text comment.</summary>
    public string? Comment { get; init; }

    /// <summary>
    /// The score's source: <c>human</c>, <c>api</c>, or <c>judge</c>. Today
    /// only <c>human</c> is used; the column is set up from the start so
    /// online evaluation (F-71) can write a judge score into the same table as-is.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>The actor who gave the score. <see langword="null"/> in an identity-less setup.</summary>
    public string? Author { get; init; }

    /// <summary>The creation/last-updated time.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}
