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

        return new(new RunJudgment
        {
            Score = score,
            Reason = hasForbiddenTerm ? "The response contains a blocked phrase." : "Deterministic quality rule.",
        });
    }
}
