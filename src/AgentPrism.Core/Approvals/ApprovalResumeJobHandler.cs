using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Handles <see cref="JobKind.ApprovalResume"/> jobs. It answers the pending tool
/// approval request in the session history for a decided <see cref="PendingApproval"/>
/// and resumes the run.
/// </summary>
/// <remarks>
/// <para>
/// The HTTP layer has already persisted the decision, who made it, and when through
/// <see cref="IPendingApprovalStore.DecideAsync"/> at the <c>decide</c> endpoint, and has
/// written it to the audit trail. This handler only feeds that decision into the
/// session as <c>ToolApprovalResponseContent</c>. It does not make a decision or write an audit entry.
/// </para>
/// <para>
/// Unlike <c>AgentRunJobHandler</c>, this job uses a <strong>new</strong> <c>RunId</c>.
/// The previous run ended with <see cref="RunStatus.AwaitingApproval"/> and never changes again.
/// If the model asks approval for another tool after it uses the approved tool, the new
/// root run also ends with <c>AwaitingApproval</c>. The chain continues in the same way.
/// </para>
/// </remarks>
internal sealed class ApprovalResumeJobHandler(
    IAgentCatalog catalog,
    AgentSessionManager sessions,
    IRunStore runStore,
    IPendingApprovalStore approvalStore,
    Microsoft.Agents.AI.ChatHistoryProvider chatHistory,
    TimeProvider? timeProvider = null,
    ILogger<ApprovalResumeJobHandler>? logger = null) : IJobHandler
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public JobKind Kind => JobKind.ApprovalResume;

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (newRunId, approvalId, agentName) = ParsePayload(context.Job.Payload);

        var approval = await approvalStore.GetAsync(approvalId, cancellationToken).ConfigureAwait(false);

        if (approval is null || approval.Status == ApprovalStatus.Pending)
        {
            await FailQueuedRunAsync(
                newRunId,
                context.Job.TenantId,
                new AgentPrismException(
                    $"The approval request with identifier '{approvalId}' was not found or is not decided yet."),
                cancellationToken).ConfigureAwait(false);

            return;
        }

        Microsoft.Agents.AI.AIAgent agent;
        Microsoft.Agents.AI.AgentSession session;
        ChatMessage approvalMessage;

        try
        {
            agent = await catalog.ResolveAsync(agentName, culture: null, cancellationToken).ConfigureAwait(false)
                ?? throw new AgentPrismException(
                    $"The agent named '{agentName}' was not found. The job will be marked as failed.");

            session = await sessions
                .GetOrCreateSessionAsync(agent, approval.SessionId, cancellationToken)
                .ConfigureAwait(false);

            var request = await FindPendingRequestAsync(agent, session, chatHistory, approval.RequestId, cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new AgentPrismException(
                    $"The tool approval request with identifier '{approval.RequestId}' was not found in the session history " +
                    "(the session might have been reset after this request). The decision cannot be applied.");

            approvalMessage = new ChatMessage(
                ChatRole.User,
                [request.CreateResponse(approval.Status == ApprovalStatus.Approved, reason: string.Empty)]);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Same rationale as AgentRunJobHandler: RunRecordingAgent did not run,
            // so this handler must change the 'runs' row to Failed.
            await FailQueuedRunAsync(newRunId, context.Job.TenantId, exception, cancellationToken)
                .ConfigureAwait(false);

            throw;
        }

        await agent.RunAsync(
            [approvalMessage],
            session,
            new AgentPrismRunOptions
            {
                RunId = newRunId,
                SessionId = approval.SessionId,
            },
            cancellationToken).ConfigureAwait(false);

        await sessions.SaveSessionAsync(agent, session, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Finds the unanswered approval request with the supplied identifier in the session history.</summary>
    /// <remarks>
    /// Uses the same logic as <c>ToolApprovalResolver.CollectPendingRequests</c>
    /// in AgentPrism.AspNetCore, narrowed to one request. Approval is input for the
    /// next turn, and the request itself only exists in the session history
    /// (<c>ToolApprovalRequestContent.CreateResponse</c>).
    /// </remarks>
    private static async ValueTask<ToolApprovalRequestContent?> FindPendingRequestAsync(
        Microsoft.Agents.AI.AIAgent agent,
        Microsoft.Agents.AI.AgentSession session,
        Microsoft.Agents.AI.ChatHistoryProvider chatHistory,
        string requestId,
        CancellationToken cancellationToken)
    {
        // MAAI001: InvokingContext constructor is marked "for evaluation purposes only".
        // The rationale is the same as ChatHistoryReader: there is no other public
        // way to read the history, and this call only reads it. This is the only use.
#pragma warning disable MAAI001
        var context = new Microsoft.Agents.AI.ChatHistoryProvider.InvokingContext(agent, session, []);
#pragma warning restore MAAI001

        var messages = await chatHistory.InvokingAsync(context, cancellationToken).ConfigureAwait(false);

        ToolApprovalRequestContent? found = null;

        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case ToolApprovalRequestContent request
                        when string.Equals(request.RequestId, requestId, StringComparison.Ordinal):
                        found = request;
                        break;

                    case ToolApprovalResponseContent response
                        when string.Equals(response.RequestId, requestId, StringComparison.Ordinal):
                        // This request is already answered, for example by a second
                        // resume attempt, so it must not receive another response.
                        found = null;
                        break;
                }
            }
        }

        return found;
    }

    private async ValueTask FailQueuedRunAsync(
        Guid runId,
        string tenantId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            await runStore.CompleteRunAsync(
                new RunCompletion
                {
                    RunId = runId,
                    Status = RunStatus.Failed,
                    CompletedAt = _clock.GetUtcNow(),
                    Error = new RunError
                    {
                        Type = exception.GetType().Name,
                        Message = exception.Message,
                    },
                    TenantId = tenantId,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception storeException) when (storeException is not OperationCanceledException)
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    storeException,
                    "Could not change the resumed run to Failed: {RunId}.",
                    runId);
            }
        }
    }

    private static (Guid RunId, Guid ApprovalId, string AgentName) ParsePayload(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("runId", out var runIdElement) ||
            runIdElement.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(runIdElement.GetString(), out var runId))
        {
            throw new AgentPrismException("The ApprovalResume job payload must contain a valid 'runId' field.");
        }

        if (!payload.TryGetProperty("approvalId", out var approvalIdElement) ||
            approvalIdElement.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(approvalIdElement.GetString(), out var approvalId))
        {
            throw new AgentPrismException("The ApprovalResume job payload must contain a valid 'approvalId' field.");
        }

        if (!payload.TryGetProperty("agentName", out var agentNameElement) ||
            agentNameElement.ValueKind != JsonValueKind.String ||
            agentNameElement.GetString() is not { Length: > 0 } agentName)
        {
            throw new AgentPrismException("The ApprovalResume job payload must contain a valid 'agentName' field.");
        }

        return (runId, approvalId, agentName);
    }
}
