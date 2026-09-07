namespace AgentPrism.Samples.CustomRunJudge;

/// <summary>A deterministic judge that checks basic response quality without a model call.</summary>
public sealed class ResponseQualityJudge : IRunJudge
{
    /// <inheritdoc />
    public string Name => "response-quality";

    /// <inheritdoc />
    public ValueTask<RunJudgment> JudgeAsync(RunJudgeContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var hasRequiredTool = context.ToolNames.Contains("knowledge-search", StringComparer.Ordinal);
        var hasForbiddenTerm = context.Output.Contains("I cannot help", StringComparison.OrdinalIgnoreCase);
        var score = hasForbiddenTerm ? 10 : context.Output.Length >= 40 && hasRequiredTool ? 100 : 65;

        // One overall verdict, named after the judge. That name is what makes it
        // the headline score, so it is the one the online-evaluation average
        // and the low-score alarm see.
        return new(new RunJudgment
        {
            Scores =
            [
                new JudgeScore
                {
                    Name = Name,
                    Kind = RunScoreKind.Numeric,
                    Value = score,
                    Comment = hasForbiddenTerm ? "The response contains a blocked phrase." : "Deterministic quality rule.",
                },
            ],
        });
    }
}
