namespace Tracon;

/// <summary>Builds judgments in the shapes the tests need.</summary>
/// <remarks>
/// A judge's <em>headline</em> score is the one named after the judge; it is
/// the only score that reaches the online-evaluation window and the judge-score
/// histogram. Most tests want exactly that, so spelling the record out at every
/// call site would bury the thing under test.
/// </remarks>
internal static class JudgeVerdict
{
    /// <summary>Builds a judgment carrying only the judge's headline score.</summary>
    /// <remarks>A <see langword="null"/> score gives an empty judgment: no decision, nothing written.</remarks>
    public static RunJudgment Headline(string judgeName, double? score, string? reason = null)
        => score is { } value
            ? new RunJudgment
            {
                Scores =
                [
                    new JudgeScore
                    {
                        Name = judgeName,
                        Kind = RunScoreKind.Numeric,
                        Value = value,
                        Comment = reason,
                    },
                ],
            }
            : new RunJudgment();
}
