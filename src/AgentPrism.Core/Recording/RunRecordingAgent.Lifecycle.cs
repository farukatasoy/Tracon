using System.Diagnostics;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Run preparation: identifier and span resolution, run scope construction, and
/// attribution reading — everything that happens before the first store write.
/// </summary>
public sealed partial class RunRecordingAgent
{
    /// <summary>
    /// Resolves the identifier of the run and opens the root span.
    /// </summary>
    /// <remarks>
    /// <strong>This method is synchronous and must stay synchronous.</strong>
    /// <see cref="Activity.Current"/> is an <c>AsyncLocal</c>: an assignment made inside
    /// an async method does not flow back to the caller when that method returns. If the
    /// span is opened here but is not held in the body of the calling method, the spans of
    /// the inner wrappers and of the model calls become <em>siblings</em> of the root span
    /// instead of <em>children</em>, and the waterfall view draws a wrong hierarchy.
    /// </remarks>
    private RunStart PrepareRun(AgentSession? session, AgentRunOptions? options, bool isStreaming)
    {
        // When the caller supplies the identifier, that one is used. A streaming endpoint
        // must know the identifier before it writes the first frame; by passing the
        // identifier that it produced itself, it can report the correct identifier to the client.
        var prismOptions = options as AgentPrismRunOptions;
        var runId = prismOptions?.RunId ?? AgentPrismId.NewId();
        var agentName = Name ?? InnerAgent.Id;
        var tenantId = _tenantContext.TenantId;

        // 🚨 Attribution is resolved HERE, in a SYNCHRONOUS body, for the same
        // reason the span and the run scope are: AmbientRunAttributionScope is an
        // AsyncLocal, and resolving it from inside an async continuation would
        // read whatever the caller's execution context happened to restore.
        var attribution = ResolveAttribution(runId);
        var sessionId = session is null ? null : GetSessionId(session);
        var depth = Math.Max(prismOptions?.Depth ?? 0, 0);
        var agentVersion = prismOptions?.AgentVersion ?? _agentVersion;

        var activity = ActivitySource.StartActivity(AgentPrismDiagnostics.RunActivityName, ActivityKind.Internal);

        if (activity is not null)
        {
            activity.SetTag(AgentPrismDiagnostics.Tags.RunId, runId);
            activity.SetTag(AgentPrismDiagnostics.Tags.AgentName, agentName);
            activity.SetTag(AgentPrismDiagnostics.Tags.TenantId, tenantId);
            activity.SetTag(AgentPrismDiagnostics.Tags.Streaming, isStreaming);

            if (sessionId is not null)
            {
                activity.SetTag(AgentPrismDiagnostics.Tags.SessionId, sessionId);
            }

            if (_modelId is not null)
            {
                activity.SetTag(AgentPrismDiagnostics.Tags.ModelId, _modelId);
            }

            if (agentVersion is { } version && _includeAgentVersionTag)
            {
                activity.SetTag(AgentPrismDiagnostics.Tags.AgentVersion, version);
            }

            if (prismOptions?.ParentRunId is { } parent)
            {
                activity.SetTag(AgentPrismDiagnostics.Tags.ParentRunId, parent);
                activity.SetTag(AgentPrismDiagnostics.Tags.Depth, depth);
            }

            // A root run starts a new trace. A child run continues the same trace: because
            // Activity.Current flows down with the call chain, the span settles under the
            // root span naturally.
            if (depth == 0)
            {
                _traceCollector?.BeginRun(activity.TraceId.ToString(), activity.SpanId.ToString());
            }
        }

        // 🚨 The writer is created IN THIS METHOD, not inside BeginRunAsync. The scope is
        // written into an AsyncLocal, and an assignment made from inside an async method
        // does not flow back to the caller; if the writer were created there, a child call
        // could not see the writer in the scope and could not write its summary events into
        // the root stream.
        var writer = new RunEventWriter(_runStore, _options, _logger, runId, _sinks);

        var scope = new AgentRunScope
        {
            RunId = runId,
            RootRunId = prismOptions?.RootRunId ?? runId,
            Depth = depth,
            AgentName = agentName,
            TenantId = tenantId,

            // The session identifier in the scope is defined more BROADLY than the one in
            // the run record: MAF does not pass a session to a child run, but the content
            // that is produced there still belongs to the root session.
            // `RunStartInfo.SessionId` (that is, runs.session_id) DOES NOT USE this fallback
            // and keeps its meaning.
            SessionId = sessionId ?? prismOptions?.SessionId,

            // Same attribution the run record gets - see the remark below on
            // 'attribution'. Read here, in the SAME synchronous body, so it is
            // visible in the scope from the moment AgentPrismRunContext.SetCurrent
            // runs.
            UserId = attribution.UserId,
            Labels = attribution.Labels,
            Budget = prismOptions?.Budget ?? (depth == 0 ? _graphOptions.CreateBudget(_timeProvider) : null),
            Writer = writer,
            ExtraUsage = new SideChannelUsageAccumulator(),
            ToolUsage = new ToolUsageAccumulator(),
            ToolAuthorization = new ToolAuthorizationAccumulator(),
            FallbackAttribution = new FallbackModelAttribution(),
            AgentVersion = agentVersion,
            ExperimentId = prismOptions?.ExperimentId,
            Variant = prismOptions?.Variant,
        };

        return new RunStart(
            scope,
            writer,
            sessionId,
            isStreaming,
            activity,
            prismOptions?.ParentRunId,
            prismOptions?.Kind ?? RunKind.Agent,
            agentVersion,
            prismOptions?.ExperimentId,
            prismOptions?.Variant,

            // The lineage link is meaningful only on a ROOT run: the child calls of a replay
            // do not correspond to the child calls of the source tree.
            depth == 0 ? prismOptions?.ReplayOfRunId : null,

            // Same rationale as ReplayOfRunId immediately above.
            depth == 0 ? prismOptions?.ContinuedFromRunId : null,

            // Attribution IS written on child runs too, unlike the lineage link: a
            // per-user cost report that stopped at the root would under-report every
            // agent that calls other agents.
            attribution.UserId,
            attribution.Labels);
    }

    /// <summary>
    /// Creates the run scope. It only builds objects in memory and performs no I/O —
    /// therefore it neither throws an exception nor gets canceled.
    /// </summary>
    /// <remarks>
    /// creating the scope was DELIBERATELY separated from
    /// <see cref="WriteRunStartAsync"/> (the step that performs I/O and can be canceled).
    /// In the old single-piece <c>BeginRunAsync</c>, the scope was created only after
    /// <see cref="SaveInputAsync"/> returned SUCCESSFULLY; and <c>SaveInputAsync</c>
    /// DELIBERATELY does not swallow <see cref="OperationCanceledException"/> (so
    /// that a real cancellation is not silenced). The result: while the RunStarted event
    /// was ALREADY written to the store (a separate, EARLIER write), if the client dropped
    /// the connection in this narrow window, the exception escaped the method WITHOUT ever
    /// ENTERING the try/finally safety net of the caller — the run stayed hanging in
    /// Running forever (note that the RunReconciliationOptions default is off as well, so
    /// there is no self-healing). The scope is now created BEFORE the I/O, INSIDE the
    /// try/finally of the caller; this way the safety net can call
    /// <see cref="CompleteAsync"/> with a complete <see cref="RunScope"/> even when
    /// <see cref="WriteRunStartAsync"/> is canceled.
    /// </remarks>
    private RunScope CreateScope(RunStart start)
        => new(
            start.Writer,
            start.Activity,
            new ToolInvocationTracker(
                start.Scope.RunId,
                start.IsStreaming,
                _timeProvider,
                start.Scope.ToolUsage,
                start.Scope.TenantId,
                start.Scope.ToolAuthorization),
            start.Scope.AgentName!,
            start.Scope.TenantId!,
            _timeProvider.GetTimestamp(),
            start.Scope.Budget,
            start.Scope.ExtraUsage,
            OwnsTrace: start.Scope.Depth == 0,
            AgentVersion: start.AgentVersion,
            RunId: start.Scope.RunId,
            RootRunId: start.Scope.RootRunId,
            Depth: start.Scope.Depth,
            SessionId: start.SessionId,
            Kind: start.Kind,
            FallbackAttribution: start.Scope.FallbackAttribution);

    /// <summary>
    /// Reads the run's attribution.
    /// </summary>
    /// <remarks>
    /// Must be called from a SYNCHRONOUS body. See the note at the call site.
    /// The guarantees (a faulty implementation cannot kill the run, an oversized
    /// attribution is dropped whole rather than trimmed, the label map is frozen)
    /// live in <see cref="RunAttributionReader"/> so that every recording path —
    /// agent and workflow alike — shares ONE implementation of them.
    /// </remarks>
    private (string? UserId, IReadOnlyDictionary<string, string>? Labels) ResolveAttribution(Guid runId)
        => RunAttributionReader.Read(
            _attributionContext,
            (message, exception) => _logger.LogWarning(
                exception,
                "{Message} Run {RunId} continues and is recorded with no user and no labels.",
                message,
                runId));

    /// <summary>What is known after the root span opens and before the store is written.</summary>
    private sealed record RunStart(
        AgentRunScope Scope,
        RunEventWriter Writer,
        string? SessionId,
        bool IsStreaming,
        Activity? Activity,
        Guid? ParentRunId,
        RunKind Kind,
        int? AgentVersion,
        Guid? ExperimentId,
        string? Variant,
        Guid? ReplayOfRunId,
        Guid? ContinuedFromRunId,
        string? UserId,
        IReadOnlyDictionary<string, string>? Labels);

    /// <summary>The recording state of a single run.</summary>
    private sealed record RunScope(
        RunEventWriter Writer,
        Activity? Activity,
        ToolInvocationTracker Tools,
        string AgentName,
        string TenantId,
        long StartedAt,
        AgentRunBudget? Budget,
        SideChannelUsageAccumulator? ExtraUsage,
        bool OwnsTrace,
        int? AgentVersion,
        Guid RunId,
        Guid RootRunId,
        int Depth,
        string? SessionId,
        RunKind Kind,
        FallbackModelAttribution? FallbackAttribution);
}
