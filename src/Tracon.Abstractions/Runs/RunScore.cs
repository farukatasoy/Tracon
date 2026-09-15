namespace Tracon;

/// <summary>
/// A human (or judge) score for a run or for a single message within a run.
/// </summary>
/// <remarks>
/// <para>
/// If <see cref="MessageId"/> is empty, the score belongs to
/// <strong>the whole run</strong>; if set, it belongs to a single message.
/// </para>
/// <para>
/// An author (<see cref="Author"/>) writes the same <see cref="Name"/> onto the
/// same target (a run or a message) once — a second write updates the existing
/// row, <see cref="IRunScoreStore.UpsertAsync"/>. A <strong>different</strong>
/// name from the same author opens a new row, so one reviewer can score the
/// same run for <c>helpfulness</c> and for <c>accuracy</c> side by side. When
/// <see cref="Author"/> is empty (an identity-less setup), this rule does not
/// apply; every call opens a new row.
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

    /// <summary>
    /// The score's stable, low-cardinality name — part of the uniqueness key.
    /// </summary>
    /// <remarks>
    /// Must match <c>[A-Za-z0-9._-]{1,64}</c>, the same rule as
    /// <see cref="IRunJudge.Name"/>. It is used as a metric tag and as a score
    /// field, so it must stay low-cardinality; a run identifier or a timestamp
    /// does not belong here. <see cref="RunScoreRules"/> carries the rule.
    /// </remarks>
    public required string Name { get; init; }

    /// <summary>The shape of the value.</summary>
    public required RunScoreKind Kind { get; init; }

    /// <summary>
    /// The numeric value: 0/1 for <see cref="RunScoreKind.Binary"/>, 1 to 5 for
    /// <see cref="RunScoreKind.Stars"/>, 0 to 100 for
    /// <see cref="RunScoreKind.Numeric"/>.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> means <strong>no measurement was made</strong>,
    /// NOT zero — the same rule <see cref="JudgeScore.Value"/> already states.
    /// Always <see langword="null"/> when <see cref="Kind"/> is
    /// <see cref="RunScoreKind.Categorical"/>.
    /// </remarks>
    public double? Value { get; init; }

    /// <summary>The categorical value. Set only for a <see cref="RunScoreKind.Categorical"/> score.</summary>
    /// <remarks>At most <see cref="RunScoreRules.MaxTextValueLength"/> characters; free-form prose belongs in <see cref="Comment"/>.</remarks>
    public string? TextValue { get; init; }

    /// <summary>A free-text comment.</summary>
    public string? Comment { get; init; }

    /// <summary>
    /// The score's source: <c>human</c>, <c>api</c>, or <c>judge:{name}</c>.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>The actor who gave the score. <see langword="null"/> in an identity-less setup.</summary>
    public string? Author { get; init; }

    /// <summary>The creation/last-updated time.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// The version of the component that produced this score, when one is known.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see langword="null"/> means <strong>no version was resolved</strong> —
    /// a human or API score, or a judge that reports none. It never means
    /// "version zero", the same rule <see cref="Value"/> already states.
    /// </para>
    /// <para>
    /// A judge that bridges an evaluation package scores against that package's
    /// prompt, so upgrading the package can move a regression baseline while
    /// the model stays the same. This field is what separates the two. It is
    /// written from <see cref="RunJudgment.EvaluatorVersion"/> and is at most
    /// <see cref="RunScoreRules.MaxEvaluatorVersionLength"/> characters.
    /// </para>
    /// </remarks>
    public string? EvaluatorVersion { get; init; }
}
