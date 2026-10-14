using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Closes a run that has no session and stopped at a tool approval.
/// </summary>
/// <remarks>
/// <para>
/// An approval is the input to the next turn and is answered from the session's
/// history. A run without a session therefore can never have its request
/// answered: <c>RunRecordingAgent</c> closes it <see cref="RunStatus.AwaitingApproval"/>,
/// and no inbox row is written for it. The run is corrected to
/// <see cref="RunStatus.Failed"/> instead, so it does not wait forever.
/// </para>
/// <para>
/// The queued agent run applies the same rule in <c>AgentRunJobHandler</c>. A
/// batch item and an eval case run without a session too, and both use this.
/// So do the surfaces whose caller cannot send an approval back: the
/// Chat Completions endpoint, which carries no session, and an agent exposed over
/// MCP or A2A, which never answers one. Each still tells its own caller
/// in its own wire shape; this closes the run.
/// </para>
/// </remarks>
internal static class SessionlessApprovalRun
{
    /// <summary>The reason the run, the batch item and the eval case carry.</summary>
    public const string Message =
        "The run requested a tool approval, but it has no session; approval is the input to the next turn " +
        "and cannot be resolved without one.";

    /// <summary>The reason a run started by an external caller (MCP, A2A) carries.</summary>
    public const string ExternalMessage =
        "The run requested a tool approval, but an externally-invoked agent cannot respond to an approval request.";

    /// <summary>The error type of a run closed because nobody can answer its approval.</summary>
    /// <remarks>
    /// The MCP task store reads it: a poller sees such a run as the approval refusal, the same
    /// shape the tool call itself returned, not as a failure.
    /// </remarks>
    public const string ErrorType = "approval_unanswerable";

    /// <summary>Says whether a response stopped at a tool approval.</summary>
    /// <param name="messages">The response's messages.</param>
    /// <returns><see langword="true"/> when the response carries an approval request.</returns>
    public static bool StoppedAtApproval(IEnumerable<ChatMessage> messages)
        => ChildRunApproval.CollectRequests(messages).Count > 0;

    /// <summary>Corrects the run's status to <see cref="RunStatus.Failed"/>.</summary>
    /// <param name="runStore">The run store; <see langword="null"/> leaves the row as it is.</param>
    /// <param name="runId">The run.</param>
    /// <param name="tenantId">The tenant the run belongs to.</param>
    /// <param name="completedAt">When the run is closed.</param>
    /// <param name="logger">Logs a store that cannot close the row.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="message">The reason the run carries; <see cref="Message"/> when <see langword="null"/>.</param>
    /// <returns>A task that completes when the row is written or the failure is logged.</returns>
    /// <remarks>
    /// A store error is logged and swallowed: the item, the case or the caller is still
    /// told the run failed, and observability does not break the work.
    /// </remarks>
    public static async ValueTask CloseAsFailedAsync(
        IRunStore? runStore,
        Guid runId,
        string tenantId,
        DateTimeOffset completedAt,
        ILogger? logger,
        CancellationToken cancellationToken,
        string? message = null)
    {
        if (runStore is null)
        {
            return;
        }

        try
        {
            await runStore.CompleteRunAsync(
                new RunCompletion
                {
                    RunId = runId,
                    Status = RunStatus.Failed,
                    CompletedAt = completedAt,
                    Error = new RunError { Type = ErrorType, Message = message ?? Message },
                    TenantId = tenantId,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "Could not close run {RunId}, which waits for an approval it cannot get, as Failed.", runId);
            }
        }
    }
}
