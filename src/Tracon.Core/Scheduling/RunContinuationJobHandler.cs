using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Handles <see cref="JobHandlerKeys.RunContinuation"/> jobs: resumes an interrupted
/// run, in the SAME session, as a new run.
/// </summary>
/// <remarks>
/// <para>
/// Triggered by <see cref="RunReconciliationService"/> right after it claims
/// an orphaned run — never by a client request. The interrupted run's row
/// stays <see cref="RunStatus.Failed"/> and never changes again (the same
/// principle as <see cref="RunStatus.AwaitingApproval"/>); this handler opens
/// a NEW <c>runs</c> row whose <see cref="RunRecord.ContinuedFromRunId"/>
/// points at it.
/// </para>
/// <para>
/// The interrupted turn's ORIGINAL input is re-sent into the session — the
/// session itself never advanced past its pre-turn state, because
/// <c>AgentSessionManager.SaveSessionAsync</c> is an explicit call that runs
/// only AFTER an agent call returns, and the process crashed before that. The
/// model runs its turn again from the start, but a <see cref="RecordedToolPlayback"/>
/// wrapper (built from the interrupted run's own tool ledger, with
/// <see cref="ToolPlaybackMismatchPolicy.RunLive"/>) makes every call that
/// already completed before the crash a no-op replay instead of a repeat —
/// only calls past the interruption point actually run.
/// </para>
/// <para>
/// Only a root agent run is continued (<see cref="RunContinuationEligibility"/>):
/// a workflow run resumes from its checkpoints, and a sub-run has nobody
/// waiting for its answer.
/// </para>
/// <para>
/// A code-defined agent (no persistent <see cref="AgentDefinition"/>) cannot
/// be continued: there is no definition to recompile with the playback
/// wrapper. The same limit applies to an agent that uses skills, callable
/// sub-agents or agent extensions — all three can run through an
/// <c>AIContextProvider</c> that the tool wrapper never sees, so a completed
/// skill, sub-agent or extension tool call could not be told apart from one
/// past the interruption point; continuation is refused rather than risk a
/// silent repeat.
/// </para>
/// <para>
/// The continued turn runs under the user and labels of the interrupted run,
/// through <see cref="AmbientRunAttributionScope"/>, so its row, its tools and
/// its sub-agents carry them. It ends like any queued turn
/// (<see cref="QueuedTurn"/>): a turn that asks for approval puts the request in
/// the mailbox after the session is saved, and a store error on that save is
/// tried again, never the turn.
/// </para>
/// </remarks>
internal sealed class RunContinuationJobHandler(
    IRunStore runStore,
    IRunInputStore inputStore,
    IAgentDefinitionStore definitions,
    AgentDefinitionCompiler compiler,
    AgentSessionManager sessions,
    IEnumerable<IAgentDecorator> decorators,
    IPendingApprovalStore approvalStore,
    IOptions<TraconOptions> options,
    TimeProvider? timeProvider = null,
    ILogger<RunContinuationJobHandler>? logger = null,
    ParameterizedAgentFactory? parameterFactory = null,
    IOptionsMonitor<TraconApprovalOptions>? approvalOptionsMonitor = null,
    IWebhookPublisher? webhookPublisher = null) : IJobHandler
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    // Same ordering rationale as RunReplayService/CompositeAgentCatalog: the
    // decorator with the larger Order runs first, so the recording wrapper
    // (Order = 0) ends up outermost.
    private readonly IAgentDecorator[] _decorators = [.. decorators.OrderByDescending(static decorator => decorator.Order)];

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (sourceRunId, continuationRunId) = ParsePayload(context.Job.Payload);

        RunRecord source;

        try
        {
            source = await runStore.GetRunAsync(sourceRunId, cancellationToken).ConfigureAwait(false)
                ?? throw new TraconException(
                    $"There is no run with id '{sourceRunId}'; it cannot be continued. The job will be marked as failed.");

            // The reconciler no longer queues a continuation for a workflow
            // run or a sub-run, but a job queued before that rule - or by hand -
            // still arrives here. Refused on the kind first: a workflow named like
            // an agent would otherwise run that agent in the workflow's session.
            if (RunContinuationEligibility.Refusal(source) is { } refusal)
            {
                throw new TraconException($"Automatic continuation was refused: {refusal}");
            }
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            await FailQueuedRunAsync(continuationRunId, context.Job.TenantId, exception, cancellationToken)
                .ConfigureAwait(false);

            throw;
        }

        // 🚨 Opened in THIS method's own body, before the
        // session is opened (a session the crash left unsaved is claimed here)
        // and around the run and its save. Without it the continued turn ran
        // under no user and no labels: its row, its tools and its sub-agents.
        var (userId, labels) = QueuedRunAttribution.FromRecord(source);
        using var attribution = AmbientRunAttributionScope.Begin(userId, labels);

        Microsoft.Agents.AI.AIAgent agent;
        Microsoft.Agents.AI.AgentSession session;
        RunInputRecord input;
        string sessionId;
        int? agentVersion;
        Guid? experimentId;
        string? variant;
        IReadOnlyDictionary<string, string>? parameterValues = null;

        try
        {
            sessionId = source.SessionId
                ?? throw new TraconException(
                    $"Run '{sourceRunId}' has no session; a sessionless run has no turn to continue.");

            input = await inputStore.GetAsync(context.Job.TenantId, sourceRunId, cancellationToken).ConfigureAwait(false)
                ?? throw new TraconException(
                    $"Run '{sourceRunId}' has no recorded input (input recording may have been disabled, " +
                    "or a retention policy deleted it); it cannot be continued.");

            if (input.Messages.Count == 0)
            {
                throw new TraconException($"Run '{sourceRunId}' recorded an empty input; it cannot be continued.");
            }

            agentVersion = source.AgentVersion;

            // The version was pinned but the arm was dropped,
            // so the second half of an experiment run counted in no arm.
            experimentId = source.ExperimentId;
            variant = source.Variant;

            var definition = (agentVersion is { } version
                ? await definitions.GetVersionAsync(source.AgentName, version, cancellationToken).ConfigureAwait(false)
                : await definitions.GetAsync(source.AgentName, cancellationToken).ConfigureAwait(false))
                ?? throw new TraconException(
                    $"Agent '{source.AgentName}' has no persistent definition" +
                    (agentVersion is { } v ? $" version {v}" : string.Empty) +
                    "; a code-defined or deleted agent cannot be continued.");

            // The source run recorded the values it was bound with, and
            // the turn continues with the SAME ones. Without a record the turn
            // would run on the defaults (or fail on a required parameter) - a
            // different prompt halfway through the conversation - so a
            // definition that declares parameters
            // and a run that recorded none stays refused.
            if (definition.Parameters.Count > 0)
            {
                var recorded = parameterFactory?.ResolveRecorded(definition.Parameters, input.Parameters, overrides: null)
                    ?? new RecordedValues(null, RecordedValuesProblem.NotRecorded, null);

                if (recorded.Problem == RecordedValuesProblem.NotRecorded)
                {
                    throw new TraconException(
                        $"Agent '{source.AgentName}' declares parameters, and run '{sourceRunId}' did not record the " +
                        "values it was bound with ('Tracon:RunRecording:RecordRunInput' was off, or the run predates " +
                        "parameter recording); continuing it would run with different instructions. " +
                        "This run cannot be continued.");
                }

                if (recorded.Problem == RecordedValuesProblem.Invalid)
                {
                    throw new TraconException(
                        $"Run '{sourceRunId}' cannot be continued: agent '{source.AgentName}' no longer accepts the " +
                        $"values it was bound with. {recorded.Detail}");
                }

                parameterValues = recorded.Values;
            }

            // Skills, callable sub-agents and agent extensions can run through an
            // AIContextProvider, not through the tool-calling pipeline
            // RecordedToolPlayback wraps — see the class documentation.
            if (definition.SkillNames.Count > 0 || definition.CallableAgents.Count > 0)
            {
                throw new TraconException(
                    $"Agent '{source.AgentName}' uses skills or callable sub-agents, which do not go " +
                    "through tool playback; continuing it could silently repeat a completed skill or " +
                    "sub-agent call. This run cannot be continued.");
            }

            if (definition.ExtensionNames.Count > 0)
            {
                throw new TraconException(
                    $"Agent '{source.AgentName}' uses agent extensions, whose context providers can add tools " +
                    "that do not go through tool playback; continuing it could silently repeat a completed " +
                    "tool call. This run cannot be continued.");
            }

            if (definition.Workspace is { Enabled: true })
            {
                throw new TraconException(
                    $"Agent '{source.AgentName}' has a session workspace, whose file tools come from a context " +
                    "provider and do not go through tool playback; continuing it could silently repeat a " +
                    "completed write or delete. This run cannot be continued.");
            }

            var invocations = await runStore.ListToolInvocationsAsync(sourceRunId, cancellationToken).ConfigureAwait(false);
            var playback = new RecordedToolPlayback(invocations, ToolPlaybackMismatchPolicy.RunLive);

            // 🚨 The cache (CompiledAgentCache) is DELIBERATELY skipped, the
            // same reason as RunReplayService: the played-back tools are
            // specific to this call, and caching them would corrupt later
            // normal runs of the same agent.
            Microsoft.Agents.AI.AIAgent compiled;

            if (parameterValues is not null)
            {
                compiled = await compiler
                    .CompileParameterizedAsync(definition, culture: null, parameterValues, playback.Wrap, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                var callable = await compiler.ResolveCallableAgentsAsync(definition, cancellationToken).ConfigureAwait(false);

                compiled = await compiler
                    .CompileAsync(definition, callable, playback.Wrap, culture: null, cancellationToken)
                    .ConfigureAwait(false);
            }

            agent = Decorate(compiled, ToDescriptor(definition));
            session = await sessions.GetOrCreateSessionAsync(agent, sessionId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            // The agent never resolved and RunRecordingAgent never engaged, so
            // the placeholder 'runs' row (Queued) this job's caller opened
            // must be closed here — the same rationale as AgentRunJobHandler.
            await FailQueuedRunAsync(continuationRunId, context.Job.TenantId, exception, cancellationToken)
                .ConfigureAwait(false);

            throw;
        }

        // A continued turn can ask for approval, and nothing
        // but the mailbox can answer it. Session first, approvals second, both
        // before AwaitingApproval is published; a store error on the save is
        // tried again, never the turn.
        var turn = new QueuedTurn(
            sessions, approvalStore, options, approvalOptionsMonitor, webhookPublisher, _clock, logger,
            context, continuationRunId, agent, session);

        await agent.RunAsync(
            input.Messages,
            session,
            new TraconRunOptions
            {
                RunId = continuationRunId,
                SessionId = sessionId,
                AgentVersion = agentVersion,
                ExperimentId = experimentId,
                Variant = variant,
                ContinuedFromRunId = sourceRunId,
                ParameterValues = parameterValues,
                BeforePendingApprovalIsPublished = turn.BeforePendingApprovalIsPublishedAsync,
            },
            cancellationToken).ConfigureAwait(false);

        await turn.SaveSessionAsync(cancellationToken).ConfigureAwait(false);
    }

    private Microsoft.Agents.AI.AIAgent Decorate(Microsoft.Agents.AI.AIAgent agent, AgentDescriptor descriptor)
        => AgentDecoratorPipeline.Apply(agent, descriptor, _decorators);

    // Skill names, callable agents and extensions are zeroed on purpose: a continuation
    // compiles the definition without them (the extension gate above refuses before that).
    // The skill settings stay: without skill names they change nothing.
    private static AgentDescriptor ToDescriptor(AgentDefinition definition)
        => AgentDescriptorProjection.FromDefinition(definition, AgentDefinitionOrigin.Database, "continuation") with
        {
            SkillNames = [],
            ExtensionNames = [],
            CallableAgentNames = [],
        };

    private async ValueTask FailQueuedRunAsync(
        Guid runId,
        string tenantId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var correlationId = SafeErrorText.NewCorrelationId();

        if (logger is not null && logger.IsEnabled(LogLevel.Error))
        {
            logger.LogError(exception, "Continuation run {RunId} failed. (ref: {CorrelationId})", runId, correlationId);
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
                    "Could not close the continuation run to Failed status: {RunId}.",
                    runId);
            }
        }
    }

    private static (Guid SourceRunId, Guid ContinuationRunId) ParsePayload(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("sourceRunId", out var sourceElement) ||
            sourceElement.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(sourceElement.GetString(), out var sourceRunId))
        {
            throw new TraconException("The RunContinuation job payload must contain a valid 'sourceRunId' field.");
        }

        if (!payload.TryGetProperty("continuationRunId", out var continuationElement) ||
            continuationElement.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(continuationElement.GetString(), out var continuationRunId))
        {
            throw new TraconException("The RunContinuation job payload must contain a valid 'continuationRunId' field.");
        }

        return (sourceRunId, continuationRunId);
    }
}
