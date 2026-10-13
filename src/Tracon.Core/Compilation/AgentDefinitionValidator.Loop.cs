namespace Tracon;

/// <summary>The loop step of <see cref="AgentDefinitionValidator"/>.</summary>
internal sealed partial class AgentDefinitionValidator
{
    /// <summary>The validation code of an <c>aiJudge</c> criterion whose judge model cannot be built.</summary>
    internal const string LoopJudgeUnavailableCode = "loop_judge_unavailable";

    /// <summary>
    /// Reports every problem of the definition's loop, each with its own code and
    /// path, before compilation would stop at the first one.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <param name="messages">The report's messages.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>
    /// <para>
    /// The rules are the compiler's own (<see cref="LoopEvaluatorRegistry.FindProblems"/>):
    /// a report that applied a copy of them could pass a definition the compiler
    /// then refuses. No factory registered in code runs here; a factory that
    /// fails reports through the compilation step that follows.
    /// </para>
    /// <para>
    /// With no structural problem, an <c>aiJudge</c> criterion's judge client is
    /// built through the same tenant-aware member the compiler calls, so a judge
    /// provider the tenant's egress policy forbids is reported with a path, the way
    /// the agent's own model is.
    /// </para>
    /// </remarks>
    private async ValueTask CheckLoopAsync(
        AgentDefinition definition,
        List<ValidationMessage> messages,
        CancellationToken cancellationToken)
    {
        if (definition.Harness?.Loop is not { } loop)
        {
            return;
        }

        var registry = _compiler.GetLoopEvaluators();
        var problems = registry.FindProblems(loop);

        foreach (var problem in problems)
        {
            messages.Add(new ValidationMessage
            {
                Severity = ValidationSeverity.Error,
                Code = problem.Code,
                Message = problem.Message,
                Path = problem.Path,
            });
        }

        if (problems.Count > 0)
        {
            return;
        }

        try
        {
            _ = await registry.CreateJudgeChatClientAsync(definition, cancellationToken).ConfigureAwait(false);
        }
        catch (TraconCompilationException ex)
        {
            messages.Add(new ValidationMessage
            {
                Severity = ValidationSeverity.Error,
                Code = LoopJudgeUnavailableCode,
                Message = ex.Message,
                Path = $"harness.loop.criteria[{FirstJudgeIndex(loop)}].kind",
            });
        }
    }

    private static int FirstJudgeIndex(LoopSettings loop)
    {
        for (var index = 0; index < loop.Criteria.Count; index++)
        {
            if (string.Equals(loop.Criteria[index].Kind, "aiJudge", StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return 0;
    }
}
