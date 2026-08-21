using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// The extension point that scores a completed production run.
/// </summary>
/// <remarks>
/// <para>
/// This interface does NOT wrap MAF's <c>AIJudgeLoopEvaluator</c>.
/// Measured (MAF 1.18.0): <c>LoopEvaluation</c> does not return a SCORE (only
/// <c>ShouldReinvoke</c> and <c>Feedback</c>), and <c>LoopContext</c> requires
/// a live <c>AIAgent</c> + <c>AgentSession</c>. It is not suited to scoring a
/// finished run —
/// </para>
/// <para>Registered with <c>TryAddEnumerable</c>; multiple judges may score the same run.</para>
/// <para>
/// If more than one <see cref="IRunJudge"/> is registered in a setup, the
/// online evaluation job runs all of them; each writes its own
/// <see cref="RunScore"/> row with <c>Source = judge:{Name}</c>.
/// </para>
/// </remarks>
public interface IRunJudge
{
    /// <summary>The judge's name. Written into the <see cref="RunScore.Source"/> field as <c>judge:{Name}</c>.</summary>
    string Name { get; }

    /// <summary>Scores the run.</summary>
    /// <param name="context">The context the judge sees.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The judge's verdict.</returns>
    ValueTask<RunJudgment> JudgeAsync(
        RunJudgeContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>The context the judge sees.</summary>
public sealed record RunJudgeContext
{
    /// <summary>The identifier of the run being scored.</summary>
    public required Guid RunId { get; init; }

    /// <summary>The tenant the run belongs to.</summary>
    public required string TenantId { get; init; }

    /// <summary>The name of the agent that ran.</summary>
    public required string AgentName { get; init; }

    /// <summary>
    /// The run's input.
    /// </summary>
    /// <remarks>
    /// Read from <c>run_inputs</c> (<see cref="IRunInputStore"/>);
    /// if no record exists, the run is not sampled and this type is never produced.
    /// </remarks>
    public required IReadOnlyList<ChatMessage> Input { get; init; }

    /// <summary>The run's output text.</summary>
    public required string Output { get; init; }

    /// <summary>The tool names called. Some metrics require this.</summary>
    public IReadOnlyList<string> ToolNames { get; init; } = [];
}

/// <summary>The judge's verdict.</summary>
public sealed record RunJudgment
{
    /// <summary>The score, 0-100. <see langword="null"/> if the judge could not decide.</summary>
    /// <remarks>
    /// When no decision can be made, <see langword="null"/> is returned,
    /// NOT <c>0</c>. Zero is a measurement; the absence of a measurement is not.
    /// </remarks>
    public int? Score { get; init; }

    /// <summary>A short rationale. Written into the <see cref="RunScore.Comment"/> field.</summary>
    public string? Reason { get; init; }

    /// <summary>The judge's own model usage. Included in the cost report.</summary>
    public RunUsage? JudgeUsage { get; init; }
}
