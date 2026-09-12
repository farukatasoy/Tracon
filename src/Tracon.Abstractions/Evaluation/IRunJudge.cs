using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>The extension point that scores a completed production run.</summary>
/// <remarks>
/// <para>
/// Implementations are singleton services. The same instance can receive
/// concurrent calls, so it must be thread-safe and must not keep per-run state
/// in instance fields.
/// </para>
/// <para>
/// A call can still be repeated for the same run: once when another judge
/// causes the whole job to retry (a judge that already wrote a score for this
/// run is NOT called again on that retry — only a judge that has not yet
/// scored the run runs), and always when a caller re-triggers manual scoring
/// via <c>POST /api/runs/{id}/judge</c>. Implementations must make side
/// effects idempotent. <see cref="Name"/> is a stable low-cardinality
/// identifier. It must match <c>[A-Za-z0-9._-]{1,64}</c> and is used in metric
/// tags and score fields.
/// </para>
/// <para>
/// The supplied cancellation token is the call budget. Do not throw an
/// <see cref="OperationCanceledException"/> for an implementation-owned timeout.
/// Tracon applies its own timeout to each call.
/// </para>
/// <para>
/// <see cref="RunJudgeContext.TenantId"/> is authoritative. A judge that starts
/// an Tracon run must set its run kind to <see cref="RunKind.Eval"/>. A
/// model-backed judge uses <see cref="IModelProviderRegistry.CreateSetupChatClientAsync(ModelBinding, CancellationToken)"/>.
/// </para>
/// </remarks>
public interface IRunJudge
{
    /// <summary>The stable judge name, written as <c>judge:{Name}</c> in score fields.</summary>
    string Name { get; }

    /// <summary>Scores the run.</summary>
    /// <param name="context">The context the judge sees.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The judge's verdict.</returns>
    ValueTask<RunJudgment> JudgeAsync(
        RunJudgeContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>The limited context supplied to a judge.</summary>
/// <remarks>
/// <para>The output is non-empty but can contain only whitespace. Tool names are distinct.</para>
/// <para>This type does not include tool arguments or results, intermediate steps, run status, duration, errors, message identifiers, or session history.</para>
/// </remarks>
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

    /// <summary>The distinct tool names called by the run.</summary>
    public IReadOnlyList<string> ToolNames { get; init; } = [];
}

/// <summary>The judge's verdict: the named scores it produced.</summary>
/// <remarks>
/// <para>
/// A judge that reports one overall verdict returns a single
/// <see cref="JudgeScore"/> named after itself. A judge that bridges a metric
/// catalog returns one entry per metric. Each entry becomes its own
/// <see cref="RunScore"/> row.
/// </para>
/// <para>
/// An <strong>empty</strong> list means the judge reached no decision, and
/// nothing is written. It is not an error and it is not a score of zero.
/// </para>
/// </remarks>
public sealed record RunJudgment
{
    /// <summary>The greatest stored reason length.</summary>
    public const int MaxReasonLength = 4000;

    /// <summary>The scores the judge produced. Empty when it reached no decision.</summary>
    /// <remarks>
    /// Names must be distinct within one judgment; <see cref="JudgeScore.Name"/>
    /// is part of the stored uniqueness key, so a repeated name would overwrite
    /// the earlier row. A duplicate is reported as a contract failure and
    /// nothing is written for that judge.
    /// </remarks>
    public IReadOnlyList<JudgeScore> Scores { get; init; } = [];
}

/// <summary>A normalized judge failure returned by manual scoring.</summary>
public sealed record JudgeFailure
{
    /// <summary>The judge that failed.</summary>
    public required string JudgeName { get; init; }

    /// <summary>The stable failure code.</summary>
    public required string ErrorType { get; init; }

    /// <summary>Whether retrying the job can help.</summary>
    public required bool IsRetryable { get; init; }
}

/// <summary>The result of manually scoring a run.</summary>
public sealed record JudgeRunResponse
{
    /// <summary>The score rows that were written.</summary>
    public IReadOnlyList<RunScore> Scores { get; init; } = [];

    /// <summary>The normalized failures reported by judges.</summary>
    public IReadOnlyList<JudgeFailure> Failures { get; init; } = [];
}
