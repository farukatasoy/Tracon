using System.Diagnostics;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Run preparation: identifier and span resolution, run scope construction, and
/// attribution reading — everything that happens before the first store write.
/// </summary>
public sealed partial class RunRecordingAgent
{
    /// <summary>
    /// Returns the options to hand to the inner agent.
    /// </summary>
    /// <param name="options">The options the caller passed to this agent.</param>
    /// <returns>
    /// A plain <see cref="AgentRunOptions"/> with the base properties of
    /// <paramref name="options"/> when it is a <see cref="TraconRunOptions"/>;
    /// otherwise <paramref name="options"/> itself.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Microsoft Agent Framework's function-invocation middleware
    /// (<c>AIAgentBuilder.Use</c> with a <c>FunctionInvocationContext</c>
    /// callback) accepts only <see langword="null"/>, a plain
    /// <see cref="AgentRunOptions"/>, or the sealed <c>ChatClientAgentRunOptions</c>.
    /// It throws <see cref="NotSupportedException"/> for any other type. Every
    /// Tracon endpoint passes <see cref="TraconRunOptions"/>, so a consumer
    /// decorator that added the middleware would fail every run.
    /// </para>
    /// <para>
    /// This agent is the only reader of <see cref="TraconRunOptions"/>: its
    /// fields reach the inner agents through <see cref="TraconRunContext"/>.
    /// A plain type, not <c>ChatClientAgentRunOptions</c>, goes inward because
    /// every <see cref="AIAgent"/> accepts it, and MAF's middleware converts it
    /// itself. A decorator outside this one (<c>Order</c> below 0) still sees
    /// <see cref="TraconRunOptions"/>.
    /// </para>
    /// <para>
    /// The exceptions are a run that a workflow step was given model options for
    /// (a handoff step's tools and instructions) and a run the caller gave
    /// <see cref="TraconRunOptions.AdditionalInstructions"/>: those go inward on
    /// <c>ChatClientAgentRunOptions</c>, the only type the agent reads them from. When both are
    /// present the text of the workflow step comes first and the caller's follows, on a new line,
    /// as Microsoft Agent Framework joins an agent's instructions and a run's.
    /// </para>
    /// </remarks>
    // The copy names each base property by hand. RunOptionsHandoffTests fails
    // when Microsoft Agent Framework adds one, so the copy cannot silently drop
    // it.
    internal static AgentRunOptions? ForInnerAgent(AgentRunOptions? options)
    {
        if (options is not TraconRunOptions traconOptions)
        {
            return options;
        }

        // Cloned first: the caller's own object is never changed.
        var chatOptions = traconOptions.ChatOptions?.Clone();

        if (!string.IsNullOrWhiteSpace(traconOptions.AdditionalInstructions))
        {
            chatOptions ??= new Microsoft.Extensions.AI.ChatOptions();
            chatOptions.Instructions = string.IsNullOrWhiteSpace(chatOptions.Instructions)
                ? traconOptions.AdditionalInstructions
                : $"{chatOptions.Instructions}\n{traconOptions.AdditionalInstructions}";
        }

        AgentRunOptions inner = chatOptions is null
            ? new AgentRunOptions()
            : new ChatClientAgentRunOptions(chatOptions);

        // MEAI001: Microsoft.Extensions.AI marks the continuation token
        // experimental. It is copied, not read: dropping it would silently
        // change what the caller asked for.
#pragma warning disable MEAI001
        inner.ContinuationToken = traconOptions.ContinuationToken;
#pragma warning restore MEAI001
        inner.AllowBackgroundResponses = traconOptions.AllowBackgroundResponses;
        inner.AdditionalProperties = traconOptions.AdditionalProperties?.Clone();
        inner.ResponseFormat = traconOptions.ResponseFormat;

        return inner;
    }

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
        var runOptions = options as TraconRunOptions;
        var runId = runOptions?.RunId ?? TraconId.NewId();
        var agentName = Name ?? InnerAgent.Id;
        var tenantId = _services.TenantContext.TenantId;

        // 🚨 Attribution is resolved HERE, in a SYNCHRONOUS body, for the same
        // reason the span and the run scope are: AmbientRunAttributionScope is an
        // AsyncLocal, and resolving it from inside an async continuation would
        // read whatever the caller's execution context happened to restore.
        var attribution = ResolveAttribution(runId);
        var sessionId = session is null ? null : GetSessionId(session);
        var depth = Math.Max(runOptions?.Depth ?? 0, 0);
        var agentVersion = runOptions?.AgentVersion ?? AgentVersion;

        var activity = ActivitySource.StartActivity(TraconDiagnostics.RunActivityName, ActivityKind.Internal);

        if (activity is not null)
        {
            activity.SetTag(TraconDiagnostics.Tags.RunId, runId);
            activity.SetTag(TraconDiagnostics.Tags.AgentName, agentName);
            activity.SetTag(TraconDiagnostics.Tags.TenantId, tenantId);
            activity.SetTag(TraconDiagnostics.Tags.Streaming, isStreaming);

            if (sessionId is not null)
            {
                activity.SetTag(TraconDiagnostics.Tags.SessionId, sessionId);
            }

            if (_identity.ModelId is not null)
            {
                activity.SetTag(TraconDiagnostics.Tags.ModelId, _identity.ModelId);
            }

            if (agentVersion is { } version && _services.IncludeAgentVersionTag)
            {
                activity.SetTag(TraconDiagnostics.Tags.AgentVersion, version);
            }

            if (runOptions?.ParentRunId is { } parent)
            {
                activity.SetTag(TraconDiagnostics.Tags.ParentRunId, parent);
                activity.SetTag(TraconDiagnostics.Tags.Depth, depth);
            }

            // A root run starts a new trace. A child run continues the same trace: because
            // Activity.Current flows down with the call chain, the span settles under the
            // root span naturally.
            if (depth == 0)
            {
                _services.TraceCollector?.BeginRun(activity.TraceId.ToString(), activity.SpanId.ToString());
            }
        }

        // 🚨 The writer is created IN THIS METHOD, not inside BeginRunAsync. The scope is
        // written into an AsyncLocal, and an assignment made from inside an async method
        // does not flow back to the caller; if the writer were created there, a child call
        // could not see the writer in the scope and could not write its summary events into
        // the root stream.
        var writer = _services.CreateWriter(runId, _logger, runIdIsNew: runOptions?.RunId is null);

        var budget = runOptions?.Budget ?? (depth == 0 ? _services.AgentGraph.CreateBudget(_services.TimeProvider) : null);

        // The root names the counter its whole tree adds to, once, on the budget every
        // run in the tree shares. A child that ends after the root, in a task that carries
        // neither the root's tenant nor its user, reads the identity from here when it closes.
        // Assigned afresh by every root, so a reused budget never keeps an earlier root's tenant.
        if (depth == 0 && budget is not null)
        {
            budget.Account = new TreeAccount(tenantId, agentName, attribution.UserId, _services.QuotaEnforcer);
        }

        var scope = new AgentRunScope
        {
            RunId = runId,
            RootRunId = runOptions?.RootRunId ?? runId,
            Depth = depth,
            AgentName = agentName,
            TenantId = tenantId,

            // The session identifier in the scope is defined more BROADLY than the one in
            // the run record: MAF does not pass a session to a child run, but the content
            // that is produced there still belongs to the root session.
            // `RunStartInfo.SessionId` (that is, runs.session_id) DOES NOT USE this fallback
            // and keeps its meaning.
            SessionId = sessionId ?? runOptions?.SessionId,

            // Same attribution the run record gets - see the remark below on
            // 'attribution'. Read here, in the SAME synchronous body, so it is
            // visible in the scope from the moment TraconRunContext.SetCurrent
            // runs.
            UserId = attribution.UserId,
            Labels = attribution.Labels,
            Budget = budget,

            // The culture the root was requested with, and the same value in every sub-run below it: the sub-run's
            // options carry the parent scope's value. Read from the options here, in this synchronous body, like the
            // rest of the scope.
            Culture = runOptions?.Culture,
            Writer = writer,
            ExtraUsage = new SideChannelUsageAccumulator(),

            // A fresh one for every run, a sub-run included: the model pipeline adds to the accumulator of the scope the
            // call runs in, and a sub-run's own scope keeps its calls off the run that called it.
            ObservedUsage = new ObservedUsageAccumulator(),
            ToolUsage = new ToolUsageAccumulator(),
            HostedCalls = new HostedCallLedger(),
            ContextRecord = new ContextRecordClaim(),

            // What the tools of this run measure beyond tokens adds up here and
            // reaches the tree's budget the moment it does.
            NonTokenCost = new NonTokenCostAccumulator(_services.PricingCurrency, budget),
            ToolAuthorization = new ToolAuthorizationAccumulator(),
            ToolExplainedFailures = new ToolExplainedFailureAccumulator(),

            // Made here with the rest of the scope, never by the tool chain: a layer that opened a scope in an
            // async helper would leave this ledger empty for the recorder that reads it.
            ToolCalls = new ToolCallLedger(_services.TimeProvider),
            FallbackAttribution = new FallbackModelAttribution(),
            AgentVersion = agentVersion,
            ExperimentId = runOptions?.ExperimentId,
            Variant = runOptions?.Variant,
            SuspendsWorkflowOnApproval = runOptions?.SuspendsWorkflowOnApproval ?? false,
            SessionHasNoRecord = runOptions?.SessionHasNoRecord ?? false,

            // Paired with SessionId above: the session that carries an identity, or the root's for a run below it.
            RecordSession = sessionId is not null ? session : runOptions?.RecordSession,
            AcceptsToolInput = depth == 0 && (runOptions?.AcceptsToolInput ?? false),
            ToolInputAnswers = runOptions?.ToolInputAnswers,
            ToolInputs = new ToolInputLedger(),
            AfterLoopIterationRecorded = depth == 0 ? runOptions?.AfterLoopIterationRecorded : null,
        };

        return new RunStart(
            scope,
            writer,
            sessionId,
            isStreaming,
            activity,
            runOptions?.ParentRunId,
            runOptions?.Kind ?? RunKind.Agent,
            agentVersion,
            runOptions?.ExperimentId,
            runOptions?.Variant,

            // The lineage link is meaningful only on a ROOT run: the child calls of a replay
            // do not correspond to the child calls of the source tree.
            depth == 0 ? runOptions?.ReplayOfRunId : null,

            // Same rationale as ReplayOfRunId immediately above.
            depth == 0 ? runOptions?.ContinuedFromRunId : null,

            // Attribution IS written on child runs too, unlike the lineage link: a
            // per-user cost report that stopped at the root would under-report every
            // agent that calls other agents.
            attribution.UserId,
            attribution.Labels,

            // The record of what the caller bound; see TraconRunOptions.ParameterValues. An agent the catalog
            // compiled for resolve-time values carries the same record as a marker (BoundParameterValues);
            // the run option wins when both exist.
            runOptions?.ParameterValues ?? InnerAgent.GetService<BoundParameterValues>()?.Values);
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
    /// <see cref="CompleteAsync"/> with a complete <see cref="RunRecordingScope"/> even when
    /// <see cref="WriteRunStartAsync"/> is canceled.
    /// </remarks>
    private RunRecordingScope CreateScope(RunStart start)
        => new(
            start.Writer,
            start.Activity,
            new ToolInvocationTracker(
                start.Scope.RunId,
                start.IsStreaming,
                _services.TimeProvider,
                start.Scope.ToolUsage,
                start.Scope.TenantId,
                start.Scope.ToolAuthorization,
                start.Scope.ToolExplainedFailures,
                start.Scope.ToolCalls),
            start.Scope.AgentName!,
            start.Scope.TenantId!,
            _services.TimeProvider.GetTimestamp(),
            start.Scope.Budget,
            start.Scope.ExtraUsage,
            OwnsTrace: start.Scope.Depth == 0,
            AgentVersion: start.AgentVersion,
            RunId: start.Scope.RunId,
            RootRunId: start.Scope.RootRunId,
            Depth: start.Scope.Depth,
            SessionId: start.SessionId,
            Kind: start.Kind,
            FallbackAttribution: start.Scope.FallbackAttribution,
            UserId: start.UserId,
            NonTokenCost: start.Scope.NonTokenCost,
            HostedCalls: start.Scope.HostedCalls,
            ToolCalls: start.Scope.ToolCalls,
            ObservedUsage: start.Scope.ObservedUsage);

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
            _services.AttributionContext,
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
        IReadOnlyDictionary<string, string>? Labels,
        IReadOnlyDictionary<string, string>? ParameterValues);
}
