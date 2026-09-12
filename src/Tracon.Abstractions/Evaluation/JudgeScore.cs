namespace Tracon;

/// <summary>One named score produced by an <see cref="IRunJudge"/>.</summary>
/// <remarks>
/// <para>
/// A judge returns a list of these, and Tracon writes each one as its own
/// <see cref="RunScore"/> row. <see cref="Name"/> is part of the stored
/// uniqueness key, so two scores a judge returns for the same run must not
/// share a name; the second would overwrite the first.
/// </para>
/// <para>
/// A judge's <strong>headline</strong> score is the one whose
/// <see cref="Name"/> equals <see cref="IRunJudge.Name"/>. Only the headline
/// score feeds the online-evaluation window (its average and the
/// <c>run.score.low</c> alarm) and the <c>tracon.judge.score</c>
/// histogram, because both are defined on the 0-100 quality scale. A score
/// under any other name is stored and readable, but it is never mixed into
/// that average — a judge that reports several metrics on several scales
/// would otherwise corrupt it.
/// </para>
/// <para>
/// A <see langword="null"/> <see cref="Value"/> records that the judge made
/// <strong>no measurement</strong>, which is not the same as a measurement of
/// zero. A judge that prefers to write nothing at all returns no
/// <see cref="JudgeScore"/> for that metric instead.
/// </para>
/// </remarks>
public sealed record JudgeScore
{
    /// <summary>The score's stable, low-cardinality name.</summary>
    /// <remarks>
    /// Must match <c>[A-Za-z0-9._-]{1,64}</c>; <see cref="RunScoreRules"/>
    /// carries the rule. A judge that reports a single overall verdict uses its
    /// own <see cref="IRunJudge.Name"/> here.
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
    /// <see langword="null"/> means no measurement was made, NOT zero. Always
    /// <see langword="null"/> when <see cref="Kind"/> is
    /// <see cref="RunScoreKind.Categorical"/>. A value outside its kind's range
    /// is a contract violation: the score is not written and the judge is
    /// reported as failed.
    /// </remarks>
    public double? Value { get; init; }

    /// <summary>The categorical value. Set only for a <see cref="RunScoreKind.Categorical"/> score.</summary>
    /// <remarks>At most <see cref="RunScoreRules.MaxTextValueLength"/> characters.</remarks>
    public string? TextValue { get; init; }

    /// <summary>The rationale, written into <see cref="RunScore.Comment"/>.</summary>
    /// <remarks>Truncated to <see cref="RunJudgment.MaxReasonLength"/> characters before it is stored.</remarks>
    public string? Comment { get; init; }
}
