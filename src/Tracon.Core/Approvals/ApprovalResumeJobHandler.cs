using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Handles <see cref="JobHandlerKeys.ApprovalResume"/> jobs. It answers the pending tool
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
/// root run also ends with <c>AwaitingApproval</c>, and its request goes to the same
/// mailbox, session first (<see cref="QueuedTurn"/>). The chain continues in the same way.
/// </para>
/// <para>
/// The new run runs under the user and labels of the run that asked for approval, as its
/// row already says (<c>ApprovalEndpoints</c> copies them): its tools and sub-agents read
/// them from <see cref="AmbientRunAttributionScope"/>. The approver is recorded in the
/// audit trail, not as the run's user.
/// </para>
/// <para>
/// The new run runs on the agent version of the run that asked for approval, and
/// counts in the same experiment arm: the tool call and its answer are one turn,
/// and a definition saved while the approval waited must not change it halfway.
/// An agent whose source keeps no version history (a code agent) runs its current
/// version. The culture is not stored on the run record, so the new run uses the
/// default instructions.
/// </para>
/// <para>
/// An agent that declares parameters resumes with the values the source run
/// recorded. Resuming it on the defaults would answer the tool call under
/// different instructions than the ones that asked for it, so a source that
/// recorded none (input recording off, or a run from before parameter recording)
/// is refused - the decision cannot be applied, and the message says why.
/// </para>
/// </remarks>
internal sealed class ApprovalResumeJobHandler(
    IAgentCatalog catalog,
    AgentSessionManager sessions,
    IRunStore runStore,
    IPendingApprovalStore approvalStore,
    Microsoft.Agents.AI.ChatHistoryProvider chatHistory,
    IOptions<TraconOptions> options,
    TimeProvider? timeProvider = null,
    ILogger<ApprovalResumeJobHandler>? logger = null,
    IRunInputStore? inputStore = null,
    ParameterizedAgentFactory? parameterFactory = null,
    IOptionsMonitor<TraconApprovalOptions>? approvalOptionsMonitor = null,
    IWebhookPublisher? webhookPublisher = null) : IJobHandler
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

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
                new TraconException(
                    $"The approval request with identifier '{approvalId}' was not found or is not decided yet."),
                cancellationToken).ConfigureAwait(false);

            return;
        }

        RunRecord source;

        try
        {
            source = await runStore.GetRunAsync(approval.RunId, cancellationToken).ConfigureAwait(false)
                ?? throw new TraconException(
                    $"The run '{approval.RunId}' that asked for this approval was not found; " +
                    "the version it ran on is unknown. The decision cannot be applied.");
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            await FailQueuedRunAsync(newRunId, context.Job.TenantId, exception, cancellationToken)
                .ConfigureAwait(false);

            throw;
        }

        // Opened in THIS method's own body, before the session is read
        // and around the run and its session save; an AsyncLocal write made in
        // an async helper does not flow back to its caller. Without it the
        // resumed turn's tools and sub-agents ran under no user and no labels,
        // although the run's own row carries both.
        var (userId, labels) = QueuedRunAttribution.FromRecord(source);
        using var attribution = AmbientRunAttributionScope.Begin(userId, labels);

        Microsoft.Agents.AI.AIAgent agent;
        Microsoft.Agents.AI.AgentSession session;
        ChatMessage approvalMessage;
        int? pinnedVersion;
        IReadOnlyDictionary<string, string>? parameterValues;

        try
        {
            // ApprovalResume is a built-in key a schedule can name with a
            // hand-written payload. The source run's version is pinned below,
            // so the payload's agent must be the agent that run ran.
            if (!string.Equals(source.AgentName, agentName, StringComparison.Ordinal))
            {
                throw new TraconException(
                    $"The ApprovalResume job payload names agent '{agentName}', but the run that asked for " +
                    "this approval ran a different agent. The decision cannot be applied.");
            }

            // Resolving the CURRENT version here ran the tool call's
            // answer on a definition saved while the approval waited, and the
            // run fell out of the experiment arm its first half counted in.
            Microsoft.Agents.AI.AIAgent? resolved;
            (resolved, pinnedVersion) = await SourceRunAgentResolver
                .ResolveAsync(catalog, agentName, source.AgentVersion, culture: null, cancellationToken)
                .ConfigureAwait(false);

            agent = resolved
                ?? throw new TraconException(
                    $"The agent named '{agentName}' was not found. The job will be marked as failed.");

            (agent, parameterValues) = await BindRecordedValuesAsync(
                agent, agentName, pinnedVersion, source, context.Job.TenantId, cancellationToken).ConfigureAwait(false);

            session = await sessions
                .GetOrCreateSessionAsync(agent, approval.SessionId, cancellationToken)
                .ConfigureAwait(false);

            var request = await FindPendingRequestAsync(agent, session, chatHistory, approval.RequestId, cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new TraconException(
                    $"The tool approval request with identifier '{approval.RequestId}' was not found in the session history " +
                    "(the session might have been reset after this request). The decision cannot be applied.");

            approvalMessage = new ChatMessage(
                ChatRole.User,
                [request.CreateResponse(approval.Status == ApprovalStatus.Approved, reason: string.Empty)]);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            // Same rationale as AgentRunJobHandler: RunRecordingAgent did not run,
            // so this handler must change the 'runs' row to Failed.
            await FailQueuedRunAsync(newRunId, context.Job.TenantId, exception, cancellationToken)
                .ConfigureAwait(false);

            throw;
        }

        // A resumed turn can ask for another approval, and only the
        // mailbox can answer it. The same bookkeeping as a queued run: session
        // first, approvals second, both before AwaitingApproval is published;
        // a store error on the save is tried again, never the turn.
        var turn = new QueuedTurn(
            sessions, approvalStore, options, approvalOptionsMonitor, webhookPublisher, _clock, logger,
            context, newRunId, agent, session);

        await agent.RunAsync(
            [approvalMessage],
            session,
            new TraconRunOptions
            {
                RunId = newRunId,
                SessionId = approval.SessionId,
                AgentVersion = pinnedVersion,

                // An arm names a version; a run on the current version is in none.
                ExperimentId = pinnedVersion is null ? null : source.ExperimentId,
                Variant = pinnedVersion is null ? null : source.Variant,
                ParameterValues = parameterValues,
                BeforePendingApprovalIsPublished = turn.BeforePendingApprovalIsPublishedAsync,
            },
            cancellationToken).ConfigureAwait(false);

        await turn.SaveSessionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Re-binds the resolved agent with the values the source run recorded, when
    /// the agent declares parameters.
    /// </summary>
    /// <returns>
    /// The agent to resume on and the values it was bound with
    /// (<see langword="null"/> for an agent that declares none).
    /// </returns>
    /// <exception cref="TraconException">
    /// The source recorded no values for an agent that needs them, or the agent
    /// no longer accepts the ones it recorded.
    /// </exception>
    private async ValueTask<(Microsoft.Agents.AI.AIAgent Agent, IReadOnlyDictionary<string, string>? Values)> BindRecordedValuesAsync(
        Microsoft.Agents.AI.AIAgent resolved,
        string agentName,
        int? pinnedVersion,
        RunRecord source,
        string tenantId,
        CancellationToken cancellationToken)
    {
        // The guard the compiler puts on an agent that declares a schema tells
        // us so without a store read; every other agent resumes as before.
        if (parameterFactory is null || resolved.GetService<UnboundParameterGuard>() is null)
        {
            return (resolved, null);
        }

        var (definition, origin) = await parameterFactory
            .FindAsync(agentName, pinnedVersion, cancellationToken)
            .ConfigureAwait(false);

        if (definition is not { Parameters.Count: > 0 })
        {
            return (resolved, null);
        }

        var record = inputStore is null
            ? null
            : await inputStore.GetAsync(tenantId, source.Id, cancellationToken).ConfigureAwait(false);

        var recorded = parameterFactory.ResolveRecorded(definition.Parameters, record?.Parameters, overrides: null);

        if (recorded.Problem == RecordedValuesProblem.NotRecorded)
        {
            throw new TraconException(
                $"Agent '{agentName}' declares parameters, and run '{source.Id}' did not record the values it was " +
                "bound with ('Tracon:RunRecording:RecordRunInput' was off, or the run predates parameter recording); " +
                "answering the tool call on the defaults would run under different instructions than the ones that " +
                "asked for it. The decision cannot be applied.");
        }

        if (recorded.Problem == RecordedValuesProblem.Invalid)
        {
            throw new TraconException(
                $"Agent '{agentName}' no longer accepts the values run '{source.Id}' was bound with. " +
                $"{recorded.Detail} The decision cannot be applied.");
        }

        var bound = await parameterFactory
            .CompileAsync(definition, origin, culture: null, recorded.Values, cancellationToken)
            .ConfigureAwait(false);

        return (bound, recorded.Values);
    }

    /// <summary>Finds the unanswered approval request with the supplied identifier in the session history.</summary>
    /// <remarks>
    /// Uses the same logic as <c>ToolApprovalResolver.CollectPendingRequests</c>
    /// in Tracon.AspNetCore, narrowed to one request. Approval is input for the
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
        var correlationId = SafeErrorText.NewCorrelationId();

        if (logger is not null && logger.IsEnabled(LogLevel.Error))
        {
            logger.LogError(exception, "Resumed run {RunId} failed. (ref: {CorrelationId})", runId, correlationId);
        }

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
                        Message = SafeErrorText.ForPersistence(exception, correlationId),
                    },
                    TenantId = tenantId,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception storeException) when (OperationCancellation.IsFailure(storeException, cancellationToken))
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
            throw new TraconException("The ApprovalResume job payload must contain a valid 'runId' field.");
        }

        if (!payload.TryGetProperty("approvalId", out var approvalIdElement) ||
            approvalIdElement.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(approvalIdElement.GetString(), out var approvalId))
        {
            throw new TraconException("The ApprovalResume job payload must contain a valid 'approvalId' field.");
        }

        if (!payload.TryGetProperty("agentName", out var agentNameElement) ||
            agentNameElement.ValueKind != JsonValueKind.String ||
            agentNameElement.GetString() is not { Length: > 0 } agentName)
        {
            throw new TraconException("The ApprovalResume job payload must contain a valid 'agentName' field.");
        }

        return (runId, approvalId, agentName);
    }
}
