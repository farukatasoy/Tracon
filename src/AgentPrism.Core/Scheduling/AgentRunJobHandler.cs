using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Executes <see cref="JobKind.AgentRun"/> jobs: runs a single, queued
/// (<c>Prefer: respond-async</c>) agent run from the queue.
/// </summary>
/// <remarks>
/// <para>
/// Follows the same pattern as <see cref="AgentBatchJobHandler"/>: the agent is
/// resolved through <see cref="IAgentCatalog.ResolveAsync(string, string, CancellationToken)"/>,
/// the resolved agent is already wrapped by the recording decorator, and the
/// run is written to <see cref="IRunStore"/> as a normal <c>runs</c> row.
/// </para>
/// <para>
/// The identity handed out here is the SAME identity the HTTP layer
/// promised the client through <c>Location</c> (<see cref="AgentPrismRunOptions.RunId"/>):
/// this keeps the promise of the <c>202</c> response VALID even after the job
/// actually runs from the queue. If the lease expires and the job is re-leased
/// (retry), this method is called a second time with the SAME identity; the
/// store treats <c>StartRunAsync</c> as an UPSERT (see <c>RunStartInfo.Status</c>)
/// — no new <c>runs</c> row is OPENED.
/// </para>
/// <para>
/// If the response carries a tool call awaiting approval, <see cref="RunRecordingAgent"/>
/// closes the run with <see cref="RunStatus.AwaitingApproval"/> (valid behavior
/// for ALL root runs, not specific to the queue path alone —
/// see <c>RunRecordingAgent</c>). This handler additionally, and ONLY this
/// handler, writes a <see cref="PendingApproval"/> row for each request in that
/// case (writing to this store REMAINS specific to the queue path, to
/// avoid the risk of a double-decision race); the decision is made through
/// <c>POST /api/approvals/{id}/decide</c> and queues a NEW run (see
/// <c>ApprovalResumeJobHandler</c>).
/// </para>
/// </remarks>
internal sealed class AgentRunJobHandler(
    IAgentCatalog catalog,
    AgentSessionManager sessions,
    IRunStore runStore,
    IPendingApprovalStore approvalStore,
    IOptions<AgentPrismOptions> options,
    IOptionsMonitor<AgentPrismApprovalOptions>? approvalOptionsMonitor = null,
    IWebhookPublisher? webhookPublisher = null,
    TimeProvider? timeProvider = null,
    ILogger<AgentRunJobHandler>? logger = null) : IJobHandler
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public JobKind Kind => JobKind.AgentRun;

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (runId, message, sessionId) = ParsePayload(context.Job.Payload);

        Microsoft.Agents.AI.AIAgent agent;
        Microsoft.Agents.AI.AgentSession? session;

        try
        {
            agent = await catalog.ResolveAsync(context.Job.TargetName, culture: null, cancellationToken).ConfigureAwait(false)
                ?? throw new AgentPrismException(
                    $"No agent named '{context.Job.TargetName}' was found. The job will be marked as failed.");

            session = string.IsNullOrWhiteSpace(sessionId)
                ? null
                : await sessions.GetOrCreateSessionAsync(agent, sessionId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 🚨 The agent never resolved: RunRecordingAgent never engaged, so
            // we must close the 'runs' row (Queued) that the 202 promised HERE,
            // to Failed -- otherwise the client sees 'Queued' forever through
            // GET /api/runs/{id}. Real run failures (AFTER the agent resolves)
            // do not enter this block; RunRecordingAgent already closes those
            // as Failed (see RunRecordingAgent.CompleteAsync).
            await FailQueuedRunAsync(runId, context.Job.TenantId, exception, cancellationToken)
                .ConfigureAwait(false);

            throw;
        }

        List<ChatMessage> messages = [new(ChatRole.User, message)];

        var response = await agent.RunAsync(
            messages,
            session,
            new AgentPrismRunOptions { RunId = runId },
            cancellationToken).ConfigureAwait(false);

        if (session is not null)
        {
            await sessions.SaveSessionAsync(agent, session, cancellationToken).ConfigureAwait(false);
        }

        var pendingRequests = CollectPendingApprovalRequests(response.Messages);

        if (pendingRequests.Count == 0)
        {
            return;
        }

        if (session is null)
        {
            // Approval is the input to the next turn and can ONLY be resolved
            // from session history; a run without a session can never have its
            // pending request answered. See AgentEndpoints.RunAsync for the
            // same constraint on the synchronous path ("session required for
            // approval"). RunRecordingAgent already closed this run with
            // AwaitingApproval; it is CORRECTED to Failed here.
            await FailQueuedRunAsync(
                runId,
                context.Job.TenantId,
                new AgentPrismException(
                    "The queued run requested approval, but no 'sessionId' was given; " +
                    "approval is the input to the next turn and cannot be resolved without a session."),
                cancellationToken).ConfigureAwait(false);

            return;
        }

        var expiration = approvalOptionsMonitor?.CurrentValue.DefaultExpiration ?? TimeSpan.FromHours(24);
        var now = _clock.GetUtcNow();

        foreach (var request in pendingRequests)
        {
            var approval = new PendingApproval
            {
                Id = AgentPrismId.NewId(),
                TenantId = context.Job.TenantId,
                RunId = runId,
                SessionId = sessionId!,
                RequestId = request.RequestId,
                ToolName = request.ToolCall is FunctionCallContent call ? call.Name : "unknown",
                Arguments = options.Value.RunRecording.RecordToolPayloads && request.ToolCall is FunctionCallContent argsCall
                    ? FormatArguments(argsCall)
                    : null,
                Status = ApprovalStatus.Pending,
                ExpiresAt = now + expiration,
                CreatedAt = now,
            };

            await approvalStore.CreateAsync(approval, cancellationToken).ConfigureAwait(false);

            if (webhookPublisher is not null)
            {
                await webhookPublisher.PublishAsync(
                    context.Job.TenantId,
                    WebhookEvents.ApprovalPending,
                    new WebhookEventPayload
                    {
                        OccurredAt = now,
                        Approval = new WebhookApprovalSummary
                        {
                            RequestId = approval.Id.ToString(),
                            RunId = approval.RunId.ToString(),
                            ToolName = approval.ToolName,
                        },
                    },
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static List<ToolApprovalRequestContent> CollectPendingApprovalRequests(
        IEnumerable<ChatMessage> messages)
    {
        List<ToolApprovalRequestContent>? requests = null;

        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is ToolApprovalRequestContent request)
                {
                    (requests ??= []).Add(request);
                }
            }
        }

        return requests ?? [];
    }

    private static string? FormatArguments(FunctionCallContent call)
    {
        if (call.Arguments is null || call.Arguments.Count == 0)
        {
            return null;
        }

        // SAME rationale as RunRecordingAgent.FormatArguments: formatted by
        // hand to stay AOT compatible; reflection-based JSON serialization is
        // not used.
        return string.Join(", ", call.Arguments.Select(static pair => $"{pair.Key}={pair.Value}"));
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

                    // The job's own tenant; the party that enqueued it wrote it (K-355).
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
                    "Could not close the queued run to Failed status: {RunId}.",
                    runId);
            }
        }
    }

    private static (Guid RunId, string Message, string? SessionId) ParsePayload(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("runId", out var runIdElement) ||
            runIdElement.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(runIdElement.GetString(), out var runId))
        {
            throw new AgentPrismException("The AgentRun job payload must carry a valid 'runId' field.");
        }

        if (!payload.TryGetProperty("message", out var messageElement) ||
            messageElement.ValueKind != JsonValueKind.String ||
            messageElement.GetString() is not { Length: > 0 } message)
        {
            throw new AgentPrismException("The AgentRun job payload must carry a valid 'message' field.");
        }

        var sessionId = payload.TryGetProperty("sessionId", out var sessionElement) &&
            sessionElement.ValueKind == JsonValueKind.String
            ? sessionElement.GetString()
            : null;

        return (runId, message, sessionId);
    }
}
