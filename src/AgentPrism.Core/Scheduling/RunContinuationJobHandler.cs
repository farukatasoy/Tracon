using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Handles <see cref="JobKind.RunContinuation"/> jobs: resumes an interrupted
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
/// A code-defined agent (no persistent <see cref="AgentDefinition"/>) cannot
/// be continued: there is no definition to recompile with the playback
/// wrapper. The same limit applies to an agent that uses skills or callable
/// sub-agents — both run through an <c>AIContextProvider</c> that the tool
/// wrapper never sees, so a completed skill or sub-agent call could not be
/// told apart from one past the interruption point; continuation is refused
/// rather than risk a silent repeat.
/// </para>
/// </remarks>
internal sealed class RunContinuationJobHandler(
    IRunStore runStore,
    IRunInputStore inputStore,
    IAgentDefinitionStore definitions,
    AgentDefinitionCompiler compiler,
    AgentSessionManager sessions,
    IEnumerable<IAgentDecorator> decorators,
    TimeProvider? timeProvider = null,
    ILogger<RunContinuationJobHandler>? logger = null) : IJobHandler
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    // Same ordering rationale as RunReplayService/CompositeAgentCatalog: the
    // decorator with the larger Order runs first, so the recording wrapper
    // (Order = 0) ends up outermost.
    private readonly IAgentDecorator[] _decorators = [.. decorators.OrderByDescending(static decorator => decorator.Order)];

    /// <inheritdoc />
    public JobKind Kind => JobKind.RunContinuation;

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (sourceRunId, continuationRunId) = ParsePayload(context.Job.Payload);

        Microsoft.Agents.AI.AIAgent agent;
        Microsoft.Agents.AI.AgentSession session;
        RunInputRecord input;
        string sessionId;
        int? agentVersion;

        try
        {
            var source = await runStore.GetRunAsync(sourceRunId, cancellationToken).ConfigureAwait(false)
                ?? throw new AgentPrismException(
                    $"There is no run with id '{sourceRunId}'; it cannot be continued. The job will be marked as failed.");

            sessionId = source.SessionId
                ?? throw new AgentPrismException(
                    $"Run '{sourceRunId}' has no session; a sessionless run has no turn to continue.");

            input = await inputStore.GetAsync(context.Job.TenantId, sourceRunId, cancellationToken).ConfigureAwait(false)
                ?? throw new AgentPrismException(
                    $"Run '{sourceRunId}' has no recorded input (input recording may have been disabled, " +
                    "or a retention policy deleted it); it cannot be continued.");

            if (input.Messages.Count == 0)
            {
                throw new AgentPrismException($"Run '{sourceRunId}' recorded an empty input; it cannot be continued.");
            }

            agentVersion = source.AgentVersion;

            var definition = (agentVersion is { } version
                ? await definitions.GetVersionAsync(source.AgentName, version, cancellationToken).ConfigureAwait(false)
                : await definitions.GetAsync(source.AgentName, cancellationToken).ConfigureAwait(false))
                ?? throw new AgentPrismException(
                    $"Agent '{source.AgentName}' has no persistent definition" +
                    (agentVersion is { } v ? $" version {v}" : string.Empty) +
                    "; a code-defined or deleted agent cannot be continued.");

            // Skills and callable sub-agents run through an AIContextProvider,
            // not through the tool-calling pipeline RecordedToolPlayback wraps
            // — see the class documentation.
            if (definition.SkillNames.Count > 0 || definition.CallableAgentNames.Count > 0)
            {
                throw new AgentPrismException(
                    $"Agent '{source.AgentName}' uses skills or callable sub-agents, which do not go " +
                    "through tool playback; continuing it could silently repeat a completed skill or " +
                    "sub-agent call. This run cannot be continued.");
            }

            var invocations = await runStore.ListToolInvocationsAsync(sourceRunId, cancellationToken).ConfigureAwait(false);
            var playback = new RecordedToolPlayback(invocations, ToolPlaybackMismatchPolicy.RunLive);

            var callable = await compiler.ResolveCallableAgentsAsync(definition, cancellationToken).ConfigureAwait(false);

            // 🚨 The cache (CompiledAgentCache) is DELIBERATELY skipped, the
            // same reason as RunReplayService: the played-back tools are
            // specific to this call, and caching them would corrupt later
            // normal runs of the same agent.
            var compiled = await compiler
                .CompileAsync(definition, callable, playback.Wrap, culture: null, cancellationToken)
                .ConfigureAwait(false);

            agent = Decorate(compiled, ToDescriptor(definition));
            session = await sessions.GetOrCreateSessionAsync(agent, sessionId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The agent never resolved and RunRecordingAgent never engaged, so
            // the placeholder 'runs' row (Queued) this job's caller opened
            // must be closed here — the same rationale as AgentRunJobHandler.
            await FailQueuedRunAsync(continuationRunId, context.Job.TenantId, exception, cancellationToken)
                .ConfigureAwait(false);

            throw;
        }

        await agent.RunAsync(
            input.Messages,
            session,
            new AgentPrismRunOptions
            {
                RunId = continuationRunId,
                SessionId = sessionId,
                AgentVersion = agentVersion,
                ContinuedFromRunId = sourceRunId,
            },
            cancellationToken).ConfigureAwait(false);

        await sessions.SaveSessionAsync(agent, session, cancellationToken).ConfigureAwait(false);
    }

    private Microsoft.Agents.AI.AIAgent Decorate(Microsoft.Agents.AI.AIAgent agent, AgentDescriptor descriptor)
        => AgentDecoratorPipeline.Apply(agent, descriptor, _decorators);

    private static AgentDescriptor ToDescriptor(AgentDefinition definition)
        => new()
        {
            Name = definition.Name,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            Origin = AgentDefinitionOrigin.Database,
            SourceName = "continuation",
            Version = definition.Version,
            Model = definition.Model,
            ToolNames = definition.ToolNames,
            SkillNames = [],
            CallableAgentNames = [],
            UsesHarness = definition.Harness is not null,
            UpdatedAt = definition.UpdatedAt,
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
        catch (Exception storeException) when (storeException is not OperationCanceledException)
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
            throw new AgentPrismException("The RunContinuation job payload must contain a valid 'sourceRunId' field.");
        }

        if (!payload.TryGetProperty("continuationRunId", out var continuationElement) ||
            continuationElement.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(continuationElement.GetString(), out var continuationRunId))
        {
            throw new AgentPrismException("The RunContinuation job payload must contain a valid 'continuationRunId' field.");
        }

        return (sourceRunId, continuationRunId);
    }
}
