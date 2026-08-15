using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Writes every run of the wrapped agent into <see cref="IRunStore"/> as events,
/// emits its metrics and collects its spans.
/// </summary>
/// <remarks>
/// <para>
/// This is <em>not</em> a Microsoft Agent Framework middleware but a
/// <see cref="DelegatingAIAgent"/> wrapper. The reason: the MAF middleware chain is
/// specific to one agent, and <c>HarnessAgent</c> adds its own inner decorators.
/// The outer wrapper works the same way on <strong>every</strong> agent type, the harness included.
/// </para>
/// <para>
/// Tool calls are read from the <see cref="FunctionCallContent"/> and
/// <see cref="FunctionResultContent"/> contents that MAF produces; no separate hook is necessary.
/// </para>
/// <para>
/// <strong>The root span starts here.</strong> The wrapper is the outermost decorator
/// (<c>Order = 0</c>), therefore the <c>agentprism.run</c> span that it opens collects the
/// spans of the inner wrappers and of the model calls as children. This is the only way
/// to link the run identifier and the trace identifier to each other.
/// </para>
/// </remarks>
public sealed class RunRecordingAgent : DelegatingAIAgent
{
    private static readonly ActivitySource ActivitySource = new(AgentPrismDiagnostics.ActivitySourceName);

    private readonly IRunStore _runStore;
    private readonly ITenantContext _tenantContext;
    private readonly AgentPrismRunRecordingOptions _options;
    private readonly ILogger<RunRecordingAgent> _logger;
    private readonly AgentPrismMetrics? _metrics;
    private readonly RunTraceCollector? _traceCollector;
    private readonly TimeProvider _timeProvider;
    private readonly string? _modelId;
    private readonly string? _modelProvider;
    private readonly AgentPrismAgentGraphOptions _graphOptions;
    private readonly int? _agentVersion;
    private readonly bool _includeAgentVersionTag;
    private readonly IRunPricingResolver? _pricingResolver;
    private readonly QuotaEnforcer? _quotaEnforcer;
    private readonly IWebhookPublisher? _webhookPublisher;
    private readonly IRunCancellationRegistry? _cancellationRegistry;
    private readonly IRunErrorClassifier? _errorClassifier;
    private readonly IRunInputStore? _runInputStore;
    private readonly RunSampler? _runSampler;
    private readonly ContentGuardPipeline? _contentGuardPipeline;

    /// <summary>Creates a new recording wrapper.</summary>
    /// <param name="innerAgent">The wrapped agent.</param>
    /// <param name="runStore">The store that the events are written to.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <param name="options">The recording detail settings.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="metrics">The metric instruments. When <see langword="null"/>, no metric is emitted.</param>
    /// <param name="traceCollector">The span collector. When <see langword="null"/>, no span is written.</param>
    /// <param name="modelId">The model that the agent is bound to. <see langword="null"/> when unknown.</param>
    /// <param name="modelProvider">
    /// The model provider that the agent is bound to. It is used only for cost
    /// resolution and is not persisted (see <c>docs/KARARLAR.md</c> K-154).
    /// </param>
    /// <param name="timeProvider">The time source. When <see langword="null"/>, the system clock is used.</param>
    /// <param name="graphOptions">
    /// The call tree limits. When <see langword="null"/>, the defaults are used.
    /// </param>
    /// <param name="agentVersion">
    /// The current definition version that comes from the catalog summary of the agent.
    /// <see langword="null"/> when it is unknown (for example a code agent). On a run that
    /// an A/B experiment resolves, <see cref="AgentPrismRunOptions.AgentVersion"/> overrides it.
    /// </param>
    /// <param name="includeAgentVersionTag">
    /// Whether the <see cref="AgentPrismDiagnostics.Tags.AgentVersion"/> tag is added to the
    /// span and to the metrics. See <see cref="AgentPrismObservabilityOptions.IncludeAgentVersionTag"/>.
    /// </param>
    /// <param name="pricingResolver">
    /// The cost resolver. When <see langword="null"/>, no cost is calculated.
    /// </param>
    /// <param name="quotaEnforcer">
    /// The quota accountant. When <see langword="null"/>, consumption is not counted. Only
    /// <strong>root</strong> runs are counted; child runs are part of the same
    /// request and must not be counted twice.
    /// </param>
    /// <param name="webhookPublisher">
    /// The event publisher. When <see langword="null"/>, no <c>run.*</c> event is emitted.
    /// </param>
    /// <param name="cancellationRegistry">
    /// The cancellation registry. When <see langword="null"/>, the run cannot be canceled
    /// from outside (<c>POST /api/runs/{id}/cancel</c>).
    /// </param>
    /// <param name="errorClassifier">
    /// The error classifier. When <see langword="null"/>, the error class and the grouping
    /// fingerprint are not calculated (<see cref="RunError.Class"/>/<see cref="RunError.Fingerprint"/>
    /// stay empty).
    /// </param>
    /// <param name="runInputStore">
    /// The input store (phase 47). When <see langword="null"/>, or when
    /// <see cref="AgentPrismRunRecordingOptions.RecordRunInput"/> is off, the input is
    /// not written and the run cannot be replayed.
    /// </param>
    /// <param name="runSampler">
    /// The online evaluation sampler (phase 49). When <see langword="null"/>,
    /// no run is sampled.
    /// </param>
    /// <param name="contentGuardPipeline">
    /// The content guard pipeline (phase 48). When <see langword="null"/>, or when
    /// <see cref="ContentGuardPipeline.HasGuards"/> is <see langword="false"/>, the input is
    /// recorded raw. When it is supplied, the input written to the <c>RunStarted</c> event and
    /// to <see cref="IRunInputStore"/> passes through the SAME inspection as the text that
    /// <see cref="ContentGuardingChatClient"/> sends to the model — if the two diverge, masked
    /// or blocked content stays raw in the durable store (HATA-S3-006).
    /// </param>
    /// <exception cref="ArgumentNullException">When one of the required dependencies is <see langword="null"/>.</exception>
    public RunRecordingAgent(
        AIAgent innerAgent,
        IRunStore runStore,
        ITenantContext tenantContext,
        AgentPrismRunRecordingOptions options,
        ILogger<RunRecordingAgent> logger,
        AgentPrismMetrics? metrics = null,
        RunTraceCollector? traceCollector = null,
        string? modelId = null,
        string? modelProvider = null,
        TimeProvider? timeProvider = null,
        AgentPrismAgentGraphOptions? graphOptions = null,
        int? agentVersion = null,
        bool includeAgentVersionTag = true,
        IRunPricingResolver? pricingResolver = null,
        QuotaEnforcer? quotaEnforcer = null,
        IWebhookPublisher? webhookPublisher = null,
        IRunCancellationRegistry? cancellationRegistry = null,
        IRunErrorClassifier? errorClassifier = null,
        IRunInputStore? runInputStore = null,
        RunSampler? runSampler = null,
        ContentGuardPipeline? contentGuardPipeline = null)
        : base(innerAgent)
    {
        ArgumentNullException.ThrowIfNull(runStore);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _runStore = runStore;
        _tenantContext = tenantContext;
        _options = options;
        _logger = logger;
        _metrics = metrics;
        _traceCollector = traceCollector;
        _modelId = modelId;
        _modelProvider = modelProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _graphOptions = graphOptions ?? new AgentPrismAgentGraphOptions();
        _agentVersion = agentVersion;
        _includeAgentVersionTag = includeAgentVersionTag;
        _pricingResolver = pricingResolver;
        _quotaEnforcer = quotaEnforcer;
        _webhookPublisher = webhookPublisher;
        _cancellationRegistry = cancellationRegistry;
        _errorClassifier = errorClassifier;
        _runInputStore = runInputStore;
        _runSampler = runSampler;
        _contentGuardPipeline = contentGuardPipeline;
    }

    /// <inheritdoc />
    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return await base.RunCoreAsync(messages, session, options, cancellationToken).ConfigureAwait(false);
        }

        // 🚨 The root span MUST be started IN THE BODY OF THIS METHOD. Activity.Current is
        // an AsyncLocal; an assignment made inside an async helper method DOES NOT FLOW
        // BACK to the caller. Measured: when the span was opened in a helper method, the
        // inner spans (invoke_agent, chat) became siblings of the root span, not children.
        var start = PrepareRun(session, options, isStreaming: false);

        // The same AsyncLocal rule applies to the run scope as well: the skill script
        // runner and the child agent wrapper read the scope from here.
        AgentPrismRunContext.SetCurrent(start.Scope);

        // A cancellation request that arrives from outside (POST /api/runs/{id}/cancel)
        // triggers this source. The token of the caller (for example the HTTP connection
        // of the client) stays separate: the run is canceled if either one drops, but the
        // registry holds ONLY ITS OWN source.
        using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var cancellationRegistration = _cancellationRegistry?.Register(
            start.Scope.RunId,
            start.Scope.RootRunId,
            start.Scope.TenantId,
            cancellationSource);

        var scope = CreateScope(start);

        try
        {
            await WriteRunStartAsync(start, messages, cancellationToken).ConfigureAwait(false);

            var response = await base.RunCoreAsync(messages, session, options, cancellationSource.Token).ConfigureAwait(false);

            foreach (var message in response.Messages)
            {
                await WriteContentsAsync(scope, message.Contents, cancellationToken).ConfigureAwait(false);
            }

            await scope.Writer.AppendAsync(
                new RunEventDraft(RunEventType.MessageCompleted) { Text = response.Text },
                cancellationToken).ConfigureAwait(false);

            var usage = ToRunUsage(response.Usage);

            // When a child run ends by requesting approval, the record must not look
            // successful: the model returned a question that cannot be answered, not a result.
            if (start.Scope.Depth > 0 && ChildRunApproval.Describe(response.Messages) is { } pending)
            {
                await CompleteAsync(scope, RunStatus.Failed, usage, ApprovalError(pending), cancellationToken)
                    .ConfigureAwait(false);

                return response;
            }

            // When a root run (Depth == 0) ends by requesting approval, it closes with
            // AwaitingApproval instead of Completed — this is INDEPENDENT of the path
            // (management API, OpenAI-compatible endpoint, MCP, A2A) and of whether the run
            // came from the queue (HATA-S2-004/MT-MCP-023: the earlier behavior marked only
            // the queue path, the synchronous paths always said Completed - the approval
            // flag was HIDDEN COMPLETELY). However the decision is DELIVERED
            // (POST /api/approvals/{id}/decide for the queue, or the next turn of the
            // synchronous caller), the status label follows the same principle: an answered
            // run STAYS in this state (K-014, the same principle as RunStatus.AwaitingInput).
            // Writing a row into the `pending_approvals` store is a separate decision and
            // DID NOT CHANGE (K-372) - only the queue path (AgentRunJobHandler) writes it;
            // this change does not reopen the risk of a double decision race.
            if (start.Scope.Depth == 0 &&
                ChildRunApproval.Describe(response.Messages) is not null)
            {
                await CompleteAsync(scope, RunStatus.AwaitingApproval, usage, null, cancellationToken)
                    .ConfigureAwait(false);

                return response;
            }

            await CompleteAsync(scope, RunStatus.Completed, usage, null, cancellationToken)
                .ConfigureAwait(false);

            return response;
        }
        catch (OperationCanceledException)
        {
            await CompleteAsync(scope, RunStatus.Canceled, null, null, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await CompleteAsync(scope, RunStatus.Failed, null, ToRunError(ex), CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            await foreach (var passthrough in base.RunCoreStreamingAsync(messages, session, options, cancellationToken).ConfigureAwait(false))
            {
                yield return passthrough;
            }

            yield break;
        }

        // The root span starts here; for the reason see the note inside RunCoreAsync.
        var start = PrepareRun(session, options, isStreaming: true);

        AgentPrismRunContext.SetCurrent(start.Scope);

        // For the reason see the note inside RunCoreAsync: this is the source that the
        // TryCancel of the registry triggers, and it is separate from the token of the caller.
        using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var cancellationRegistration = _cancellationRegistry?.Register(
            start.Scope.RunId,
            start.Scope.RootRunId,
            start.Scope.TenantId,
            cancellationSource);

        var scope = CreateScope(start);
        UsageDetails? usage = null;
        string? pendingApproval = null;

        // On a root run, a tool call that requests approval maps to AwaitingApproval
        // instead of Completed; the reason is in the SAME note inside RunCoreAsync
        // (HATA-S2-004/MT-MCP-023 - the path/queue distinction was removed).
        var isTopLevelRun = start.Scope.Depth == 0;
        var topLevelPendingApproval = false;

        var enumerator = base.RunCoreStreamingAsync(messages, session, options, cancellationSource.Token)
            .GetAsyncEnumerator(cancellationSource.Token);

        // 🚨 HATA-S1-015: these two flags separate the case where the consumer calls
        // DisposeAsync() EARLY (the loop neither ended naturally nor exited with an
        // exception). By the async-iterator rule of C#, when the consumer calls
        // DisposeAsync() AFTER a `yield return` (and BEFORE the next MoveNextAsync), only
        // the `finally` block below runs — the code BELOW it (the CompleteAsync call of
        // the natural end) NEVER runs; disposal does not CONTINUE the rest of the method
        // with normal flow, it only runs the pending `finally` blocks. This exact failure
        // occurred in the real-time voice turn when the user sent `cancel`: a cancellation
        // that arrived while the TTS network call was still running (after a
        // `yield return`, while the consumer - VoiceConversationDriver.RespondAsync - held
        // control) forced the `await foreach` of the consumer into an early DisposeAsync(),
        // and neither `Completed` nor `Canceled` was ever written — the run stayed hanging
        // in Running permanently (the real cost is lost silently).
        var naturalEnd = false;
        var completedByCatch = false;

        try
        {
            // 🚨 HATA-S4-012: this step is DELIBERATELY INSIDE the try/finally — the reason
            // is in the documentation of WriteRunStartAsync and in the note of CreateScope.
            await WriteRunStartAsync(start, messages, cancellationToken).ConfigureAwait(false);

            while (true)
            {
                AgentResponseUpdate update;

                try
                {
                    // 🚨 The scope is rewritten on EVERY step. An AsyncLocal assignment
                    // made in the body of an async iterator does not cross the
                    // `yield return` boundary: when the call returns to the driver, the
                    // ExecutionContext is restored and the next MoveNextAsync starts with
                    // a clean context. Measured (phase 12): on a streaming run the child
                    // agent call was rejected with "run recording is off".
                    // The assignment must be made IMMEDIATELY before MoveNextAsync; doing
                    // it only outside the loop is not enough.
                    AgentPrismRunContext.SetCurrent(start.Scope);

                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        naturalEnd = true;
                        break;
                    }

                    update = enumerator.Current;
                }
                catch (OperationCanceledException)
                {
                    completedByCatch = true;
                    await CompleteAsync(scope, RunStatus.Canceled, null, null, CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex)
                {
                    completedByCatch = true;
                    await CompleteAsync(scope, RunStatus.Failed, null, ToRunError(ex), CancellationToken.None)
                        .ConfigureAwait(false);
                    throw;
                }

                foreach (var content in update.Contents)
                {
                    if (content is UsageContent usageContent)
                    {
                        usage = usageContent.Details;
                    }
                }

                pendingApproval ??= start.Scope.Depth > 0
                    ? ChildRunApproval.Describe(update.Contents)
                    : null;

                topLevelPendingApproval = topLevelPendingApproval ||
                    (isTopLevelRun && ChildRunApproval.Describe(update.Contents) is not null);

                await WriteContentsAsync(scope, update.Contents, cancellationToken).ConfigureAwait(false);

                yield return update;
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);

            // Neither a natural end (the code below completes it) nor an exception (the
            // catch above already completed it) — this is an early DisposeAsync() by the
            // consumer. It is the LAST chance to write the terminal status: the code below
            // will NEVER run after this point.
            if (!naturalEnd && !completedByCatch)
            {
                await CompleteAsync(scope, RunStatus.Canceled, ToRunUsage(usage), null, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        var streamingStatus = pendingApproval is not null
            ? RunStatus.Failed
            : topLevelPendingApproval
                ? RunStatus.AwaitingApproval
                : RunStatus.Completed;

        await CompleteAsync(
            scope,
            streamingStatus,
            ToRunUsage(usage),
            pendingApproval is null ? null : ApprovalError(pendingApproval),
            cancellationToken).ConfigureAwait(false);
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
        var prismOptions = options as AgentPrismRunOptions;
        var runId = prismOptions?.RunId ?? AgentPrismId.NewId();
        var agentName = Name ?? InnerAgent.Id;
        var tenantId = _tenantContext.TenantId;
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
                _traceCollector?.BeginRun(activity.TraceId.ToString());
            }
        }

        // 🚨 The writer is created IN THIS METHOD, not inside BeginRunAsync. The scope is
        // written into an AsyncLocal, and an assignment made from inside an async method
        // does not flow back to the caller; if the writer were created there, a child call
        // could not see the writer in the scope and could not write its summary events into
        // the root stream.
        var writer = new RunEventWriter(_runStore, _options, _logger, runId);

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
            Budget = prismOptions?.Budget ?? (depth == 0 ? _graphOptions.CreateBudget() : null),
            Writer = writer,
            ExtraUsage = new CompactionUsageAccumulator(),
            ToolUsage = new ToolUsageAccumulator(),
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
            depth == 0 ? prismOptions?.ReplayOfRunId : null);
    }

    /// <summary>
    /// Creates the run scope. It only builds objects in memory and performs no I/O —
    /// therefore it neither throws an exception nor gets canceled.
    /// </summary>
    /// <remarks>
    /// 🚨 HATA-S4-012: creating the scope was DELIBERATELY separated from
    /// <see cref="WriteRunStartAsync"/> (the step that performs I/O and can be canceled).
    /// In the old single-piece <c>BeginRunAsync</c>, the scope was created only after
    /// <see cref="SaveInputAsync"/> returned SUCCESSFULLY; and <c>SaveInputAsync</c>
    /// DELIBERATELY does not swallow <see cref="OperationCanceledException"/> (K-034 — so
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
                start.Scope.TenantId),
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
            Kind: start.Kind);

    /// <summary>
    /// Opens the run record: writes the <c>runs</c> row and the first <c>RunStarted</c>
    /// event, and saves the input into <see cref="IRunInputStore"/>.
    /// </summary>
    /// <remarks>
    /// The caller must call this method INSIDE ITS OWN try/finally safety net (see the note
    /// of <see cref="CreateScope"/>) — otherwise an <see cref="OperationCanceledException"/>
    /// that occurs in this step disappears without ever moving the run to a terminal status.
    /// </remarks>
    private async ValueTask WriteRunStartAsync(
        RunStart start,
        IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        // The input list is materialized ONCE: both the query text and the input record
        // read the same collection.
        var input = messages as IReadOnlyList<ChatMessage> ?? [.. messages];

        // 🚨 The input that goes into the record passes through the guard pipeline
        // separately: otherwise masked or blocked content stays RAW in the RunStarted event
        // and in IRunInputStore — the text that goes to the model is already masked inside
        // ContentGuardingChatClient, but this record NEVER passes through that client
        // (HATA-S3-006). ContentGuardPipeline.PreviewAsync is used, not InspectAsync,
        // because the run row (runs) does not exist yet at this point — see the
        // documentation of that method. The `messages` variable that is passed on to the
        // caller is DELIBERATELY left unchanged; the text that goes to the model must not
        // be affected by this masking.
        if (_contentGuardPipeline is { HasGuards: true } guardPipeline && guardPipeline.Options.InspectInput)
        {
            input = await ContentGuardMessageMasker
                .PreviewAsync(guardPipeline, ContentGuardDirection.Input, input, _modelId, cancellationToken)
                .ConfigureAwait(false);
        }

        await start.Writer.StartAsync(
            new RunStartInfo
            {
                RunId = start.Scope.RunId,
                AgentName = start.Scope.AgentName!,
                Kind = start.Kind,
                StartedAt = _timeProvider.GetUtcNow(),
                TenantId = start.Scope.TenantId,
                SessionId = start.SessionId,
                ModelId = _modelId,
                IsStreaming = start.IsStreaming,
                ParentRunId = start.ParentRunId,

                // On a root run the field stays empty: writing a value that points to the
                // root itself would turn the question "root or child?" into a second
                // condition in the query.
                RootRunId = start.Scope.Depth == 0 ? null : start.Scope.RootRunId,
                Depth = start.Scope.Depth,
                AgentVersion = start.AgentVersion,
                ExperimentId = start.ExperimentId,
                Variant = start.Variant,
                ReplayOfRunId = start.ReplayOfRunId,
            },
            ExtractQuery(input),
            cancellationToken).ConfigureAwait(false);

        await SaveInputAsync(start, input, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the input messages of the run into <see cref="IRunInputStore"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The record is written for <strong>every</strong> run — root, child run, eval and
    /// workflow included. A child agent call must also be replayable on its own; limiting
    /// this to the root would lose as many rows as the depth of the tree.
    /// </para>
    /// <para>
    /// 🚨 The error is <strong>swallowed</strong>: observability does not break
    /// functionality (the same contract as <see cref="IRunStore"/>). A run whose input
    /// could not be written still works; it only cannot be replayed.
    /// </para>
    /// </remarks>
    private async ValueTask SaveInputAsync(
        RunStart start,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        if (_runInputStore is null || !_options.RecordRunInput || messages.Count == 0)
        {
            return;
        }

        try
        {
            await _runInputStore.SaveAsync(
                new RunInputRecord
                {
                    RunId = start.Scope.RunId,
                    TenantId = start.Scope.TenantId!,
                    Messages = messages,
                    CreatedAt = _timeProvider.GetUtcNow(),
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Failed to save the AgentPrism run input. Run {RunId} continues normally " +
                "but will not be replayable.",
                start.Scope.RunId);
        }
    }

    /// <summary>Writes the consumption of a completed root run into the quota counters.</summary>
    /// <remarks>
    /// When the price is undefined, <see cref="QuotaConsumption.Cost"/> stays
    /// <see langword="null"/> — <strong>not</strong> zero (the phase 20 rule). Such a run
    /// does not count toward the monetary quota, but it does count toward the token quota:
    /// when the quota cannot be applied in money, it falls back to tokens.
    /// </remarks>
    private async ValueTask RecordQuotaAsync(
        RunScope scope,
        RunUsage? usage,
        RunCost? cost,
        CancellationToken cancellationToken)
    {
        if (_quotaEnforcer is null)
        {
            return;
        }

        await _quotaEnforcer.RecordAsync(
            new QuotaConsumption
            {
                TenantId = scope.TenantId,
                AgentName = scope.AgentName,
                Runs = 1,
                Tokens = usage?.TotalTokens ?? 0,
                Cost = cost is { Source: not PricingSource.Unknown }
                    ? (cost.InputCost ?? 0m) + (cost.OutputCost ?? 0m)
                    : null,
                OccurredAt = _timeProvider.GetUtcNow(),
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Makes the sampling decision for online evaluation when a root run ends (phase 49).
    /// </summary>
    /// <remarks>
    /// 🚨 <see cref="RunSampler.SampleAsync"/> already swallows its own error (see the class
    /// documentation); the <c>try/catch</c> here is a second layer of defense — sampling
    /// must NEVER AFFECT the run (the rule that observability does not break functionality).
    /// </remarks>
    private async ValueTask SampleForOnlineEvalAsync(RunScope scope, RunStatus status, CancellationToken cancellationToken)
    {
        if (_runSampler is null)
        {
            return;
        }

        try
        {
            await _runSampler.SampleAsync(
                new RunSampleRequest
                {
                    RunId = scope.RunId,
                    TenantId = scope.TenantId,
                    AgentName = scope.AgentName,
                    Kind = scope.Kind,
                    Status = status,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(exception, "The online evaluation sampling decision failed: run={RunId}.", scope.RunId);
            }
        }
    }

    /// <summary>Emits <c>run.completed</c>/<c>run.failed</c> when a root run ends.</summary>
    /// <remarks>
    /// The payload carries only a <strong>summary</strong> (K-161): the message content and
    /// the model response do not go in here. A receiver that wants the content calls
    /// <c>/api/runs/{id}</c>.
    /// </remarks>
    private async ValueTask PublishRunEventAsync(
        RunScope scope,
        RunStatus status,
        RunUsage? usage,
        RunCost? cost,
        RunError? error,
        TimeSpan elapsed,
        CancellationToken cancellationToken)
    {
        if (_webhookPublisher is null)
        {
            return;
        }

        // A canceled run is neither a success nor a failure; for a subscriber it is noise.
        var eventType = status switch
        {
            RunStatus.Completed => WebhookEvents.RunCompleted,
            RunStatus.Failed => WebhookEvents.RunFailed,
            _ => null,
        };

        if (eventType is null)
        {
            return;
        }

        await _webhookPublisher.PublishAsync(
            scope.TenantId,
            eventType,
            new WebhookEventPayload
            {
                OccurredAt = _timeProvider.GetUtcNow(),
                Run = new WebhookRunSummary
                {
                    RunId = scope.RunId.ToString(),
                    RootRunId = scope.RootRunId.ToString(),
                    SessionId = scope.SessionId,
                    AgentName = scope.AgentName,
                    ModelId = _modelId,
                    Status = status.ToString(),
                    DurationMs = (long)elapsed.TotalMilliseconds,
                    InputTokens = usage?.InputTokens,
                    OutputTokens = usage?.OutputTokens,
                    Cost = cost is { Source: not PricingSource.Unknown }
                        ? (cost.InputCost ?? 0m) + (cost.OutputCost ?? 0m)
                        : null,
                    Currency = cost?.Currency,
                    Error = error?.Message,
                },
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static RunError ApprovalError(string toolNames)
        => new()
        {
            Type = nameof(AgentPrismException),
            Message = $"The child run requested user approval for the '{toolNames}' tool. " +
                      "A child agent cannot request approval: approval is the input of the next " +
                      "turn and cannot be awaited in the middle of the call tree. Define an " +
                      "automatic approval rule for this tool, or limit the child agent to tools " +
                      "that need no approval.",
        };

    private async ValueTask CompleteAsync(
        RunScope scope,
        RunStatus status,
        RunUsage? usage,
        RunError? error,
        CancellationToken cancellationToken)
    {
        // 🚨 The classifier is called ONLY when there is an error: on a successful run
        // (error is null) it never fires on the hot path.
        if (error is not null && _errorClassifier is not null)
        {
            var classification = _errorClassifier.Classify(error);
            error = error with { Class = classification.Class, Fingerprint = classification.Fingerprint };
        }

        // Calls that end before a result arrives are closed explicitly; otherwise a tool
        // card that looks "started but not finished" would stay in the user interface.
        foreach (var unfinished in scope.Tools.DrainUnfinished("The run ended before the tool result arrived."))
        {
            await scope.Writer.RecordToolInvocationAsync(unfinished, cancellationToken).ConfigureAwait(false);
            _metrics?.RecordToolInvocation(unfinished.ToolName, succeeded: false, unfinished.Duration);
        }

        // The tokens that context compaction (summarization) produces come from a
        // side-channel call that is completely separate from the AgentResponse of the agent;
        // if they are not added to the final usage here, the cost report and the tree budget
        // stay incomplete.
        usage = MergeUsage(usage, scope.ExtraUsage?.ToRunUsage());

        // The cost is calculated HERE, from the final (merged) usage — the price is a
        // snapshot (see docs/20-MALIYET-VE-GOSTERGE-PANELI.md section 20.2): if the price
        // list changes later, the cost of this run does not change.
        var cost = _pricingResolver?.Resolve(_modelProvider, _modelId, usage);

        await scope.Writer.CompleteAsync(status, usage, error, cost, cancellationToken).ConfigureAwait(false);

        // The budget is a single object that is shared across the tree; both the root and
        // the child runs feed the same counter. Otherwise the answer to the question "what
        // did the tree spend?" would cover only the child calls.
        scope.Budget?.RecordUsage(usage?.TotalTokens ?? 0);

        var elapsed = _timeProvider.GetElapsedTime(scope.StartedAt);

        // 🚨 Quota accounting and event publication run ONLY on a root run. A child run is
        // part of the same user request; if it were counted separately, an agent tree would
        // consume the quota as fast as its depth, and a separate run.completed event would
        // be emitted for every node.
        if (scope.Depth == 0)
        {
            await RecordQuotaAsync(scope, usage, cost, cancellationToken).ConfigureAwait(false);
            await PublishRunEventAsync(scope, status, usage, cost, error, elapsed, cancellationToken).ConfigureAwait(false);
            await SampleForOnlineEvalAsync(scope, status, cancellationToken).ConfigureAwait(false);
        }

        _metrics?.RecordRun(
            scope.AgentName,
            status,
            scope.TenantId,
            _modelId,
            elapsed,
            usage,
            agentVersion: _includeAgentVersionTag ? scope.AgentVersion : null);

        // When the price is undefined (Source == Unknown), nothing is emitted: emitting an
        // unknown cost as zero would make the real spend look smaller. The cost is emitted
        // on cancellation and on failure as well (Open Question 4): the money for the
        // consumed tokens is already spent.
        if (cost is { Source: not PricingSource.Unknown } knownCost)
        {
            _metrics?.RecordCost(
                scope.AgentName,
                _modelId,
                scope.TenantId,
                (knownCost.InputCost ?? 0m) + (knownCost.OutputCost ?? 0m),
                knownCost.Currency ?? "unknown");
        }

        if (scope.Activity is null)
        {
            return;
        }

        scope.Activity.SetTag(AgentPrismDiagnostics.Tags.Status, status.ToString());

        if (error is not null)
        {
            scope.Activity.SetStatus(ActivityStatusCode.Error, error.Message);
        }

        // The span is stopped BEFORE it is handed to the collector: stopping triggers the
        // ActivityStopped event, and the root span itself also enters the buffer.
        scope.Activity.Stop();

        // 🚨 ONLY the root run owns the trace buffer. Every run in the tree shares the same
        // W3C trace identifier (child spans settle under the root span) and the buffer is
        // keyed by that identifier. If a child run closed the buffer too -- and it finishes
        // FIRST -- the spans of the whole tree would attach to the child run and the root
        // run would stay empty. Measured (phase 12): on a real call the /trace endpoint of
        // the root returned 404 while the one of the child run was full.
        if (_traceCollector is not null && scope.OwnsTrace)
        {
            await _traceCollector.CompleteRunAsync(
                scope.Activity.TraceId.ToString(),
                scope.Writer.RunId,
                scope.TenantId,
                status,
                cancellationToken).ConfigureAwait(false);
        }

        scope.Activity.Dispose();
    }

    private async ValueTask WriteContentsAsync(
        RunScope scope,
        IEnumerable<AIContent> contents,
        CancellationToken cancellationToken)
    {
        foreach (var content in contents)
        {
            switch (content)
            {
                case TextContent text when _options.RecordMessageDeltas && !string.IsNullOrEmpty(text.Text):
                    await scope.Writer.AppendAsync(
                        new RunEventDraft(RunEventType.MessageDelta) { Text = text.Text },
                        cancellationToken).ConfigureAwait(false);
                    break;

                case FunctionCallContent call:
                    await WriteToolCallAsync(scope, call, cancellationToken).ConfigureAwait(false);
                    break;

                case FunctionResultContent result:
                    await WriteToolResultAsync(scope, result, cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    break;
            }
        }
    }

    private static async ValueTask WriteToolCallAsync(
        RunScope scope,
        FunctionCallContent call,
        CancellationToken cancellationToken)
    {
        var arguments = FormatArguments(call);

        scope.Tools.OnCall(call, source: null, arguments);

        await scope.Writer.AppendAsync(
            new RunEventDraft(RunEventType.ToolInvoking)
            {
                ToolName = call.Name,
                ToolCallId = call.CallId,
                Payload = arguments,
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteToolResultAsync(
        RunScope scope,
        FunctionResultContent result,
        CancellationToken cancellationToken)
    {
        var record = scope.Tools.OnResult(result);

        await scope.Writer.AppendAsync(
            new RunEventDraft(result.Exception is null ? RunEventType.ToolInvoked : RunEventType.ToolFailed)
            {
                ToolName = record.ToolName,
                ToolCallId = result.CallId,
                Text = result.Exception?.Message,
                Payload = result.Result?.ToString(),
            },
            cancellationToken).ConfigureAwait(false);

        await scope.Writer.RecordToolInvocationAsync(record, cancellationToken).ConfigureAwait(false);

        _metrics?.RecordToolInvocation(record.ToolName, record.Succeeded, record.Duration);
    }

    private static string? FormatArguments(FunctionCallContent call)
    {
        if (call.Arguments is null || call.Arguments.Count == 0)
        {
            return null;
        }

        // The arguments are formatted by hand to stay AOT compatible; JSON serialization
        // that relies on reflection is not used.
        return string.Join(", ", call.Arguments.Select(static pair => $"{pair.Key}={pair.Value}"));
    }

    // AgentSessionManager stamps the session identifier onto the session. When the stamp is
    // absent, the session was opened outside AgentPrism; instead of writing a placeholder
    // value into the record, the field is left empty.
    private static string? GetSessionId(AgentSession session) => AgentSessionIdentity.GetId(session);

    // Phase 45 (F-53): the first user message that triggered this run is written into the
    // RunEventType.RunStarted event. run_events is the ONLY place where the input text is
    // PERSISTED — the session is saved only when the run completes SUCCESSFULLY
    // (AgentEndpoints.AgentRunStream), therefore the query of a failed run cannot be read
    // through any other path.
    private static string? ExtractQuery(IReadOnlyList<ChatMessage> messages)
        => messages.FirstOrDefault(static message => message.Role == ChatRole.User)?.Text;

    private static RunUsage? MergeUsage(RunUsage? primary, RunUsage? extra)
    {
        if (extra is null)
        {
            return primary;
        }

        if (primary is null)
        {
            return extra;
        }

        return new RunUsage
        {
            InputTokens = (primary.InputTokens ?? 0) + (extra.InputTokens ?? 0),
            OutputTokens = (primary.OutputTokens ?? 0) + (extra.OutputTokens ?? 0),
            TotalTokens = (primary.TotalTokens ?? 0) + (extra.TotalTokens ?? 0),
        };
    }

    private static RunUsage? ToRunUsage(UsageDetails? usage)
        => usage is null
            ? null
            : new RunUsage
            {
                InputTokens = usage.InputTokenCount,
                OutputTokens = usage.OutputTokenCount,
                TotalTokens = usage.TotalTokenCount,
            };

    // AgentPrism exceptions can carry their own stable error type name (for example
    // content_filtered). The default value is still the full name of the type, so the shape
    // of the existing records does not change.
    private static RunError ToRunError(Exception exception)
        => new()
        {
            Type = exception is AgentPrismException prismException
                ? prismException.ErrorType
                : exception.GetType().FullName ?? exception.GetType().Name,
            Message = exception.Message,
        };

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
        Guid? ReplayOfRunId);

    /// <summary>The recording state of a single run.</summary>
    private sealed record RunScope(
        RunEventWriter Writer,
        Activity? Activity,
        ToolInvocationTracker Tools,
        string AgentName,
        string TenantId,
        long StartedAt,
        AgentRunBudget? Budget,
        CompactionUsageAccumulator? ExtraUsage,
        bool OwnsTrace,
        int? AgentVersion,
        Guid RunId,
        Guid RootRunId,
        int Depth,
        string? SessionId,
        RunKind Kind);
}
