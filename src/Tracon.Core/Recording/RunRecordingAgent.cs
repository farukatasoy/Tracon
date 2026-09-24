using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

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
/// (<c>Order = 0</c>), therefore the <c>tracon.run</c> span that it opens collects the
/// spans of the inner wrappers and of the model calls as children. This is the only way
/// to link the run identifier and the trace identifier to each other.
/// </para>
/// <para>
/// Tracon builds this wrapper around every agent that <see cref="IAgentCatalog"/> resolves;
/// resolve the agent through the catalog instead of constructing the wrapper.
/// </para>
/// </remarks>
public sealed partial class RunRecordingAgent : DelegatingAIAgent
{
    private static readonly ActivitySource ActivitySource = new(TraconDiagnostics.ActivitySourceName);

    private readonly IRunStore _runStore;
    private readonly ITenantContext _tenantContext;
    private readonly TraconRunRecordingOptions _options;
    private readonly ILogger<RunRecordingAgent> _logger;
    private readonly TraconMetrics? _metrics;
    private readonly RunTraceCollector? _traceCollector;
    private readonly TimeProvider _timeProvider;
    private readonly string? _modelId;
    private readonly string? _modelProvider;
    private readonly TraconAgentGraphOptions _graphOptions;
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
    private readonly IRunAttributionContext? _attributionContext;
    private readonly IReadOnlyList<IRunEventSink> _sinks;
    private readonly ToolApprovalPresenterRunner? _approvalPresenterRunner;

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
    /// resolution and is not persisted.
    /// </param>
    /// <param name="timeProvider">The time source. When <see langword="null"/>, the system clock is used.</param>
    /// <param name="graphOptions">
    /// The call tree limits. When <see langword="null"/>, the defaults are used.
    /// </param>
    /// <param name="agentVersion">
    /// The current definition version that comes from the catalog summary of the agent.
    /// <see langword="null"/> when it is unknown (for example a code agent). On a run that
    /// an A/B experiment resolves, <see cref="TraconRunOptions.AgentVersion"/> overrides it.
    /// </param>
    /// <param name="includeAgentVersionTag">
    /// Whether the <see cref="TraconDiagnostics.Tags.AgentVersion"/> tag is added to the
    /// span and to the metrics. See <see cref="TraconObservabilityOptions.IncludeAgentVersionTag"/>.
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
    /// The input store. When <see langword="null"/>, or when
    /// <see cref="TraconRunRecordingOptions.RecordRunInput"/> is off, the input is
    /// not written and the run cannot be replayed.
    /// </param>
    /// <param name="runSampler">
    /// The online evaluation sampler. When <see langword="null"/>,
    /// no run is sampled.
    /// </param>
    /// <param name="contentGuardPipeline">
    /// The content guard pipeline. When <see langword="null"/>, or when
    /// <see cref="ContentGuardPipeline.HasGuards"/> is <see langword="false"/>, the input is
    /// recorded raw. When it is supplied, the input written to the <c>RunStarted</c> event and
    /// to <see cref="IRunInputStore"/> passes through the SAME inspection as the text that
    /// <see cref="ContentGuardingChatClient"/> sends to the model — if the two diverge, masked
    /// or blocked content stays raw in the durable store.
    /// </param>
    /// <param name="attributionContext">
    /// The attribution context. When <see langword="null"/>, the run records no
    /// user and no labels — the same outcome as the built-in
    /// <see cref="DefaultRunAttributionContext"/> with no ambient scope open.
    /// </param>
    /// <param name="sinks">
    /// The run event observers. When <see langword="null"/> or empty, every
    /// event goes to <paramref name="runStore"/> only — the identical hot path as before
    /// this extension point existed.
    /// </param>
    /// <param name="approvalPresenterRunner">
    /// Resolves the <see cref="ToolApprovalPresentation"/> of a pending tool call. When
    /// <see langword="null"/>, no presentation is resolved — identical behavior to before
    /// <see cref="IToolApprovalPresenter"/> existed.
    /// </param>
    /// <exception cref="ArgumentNullException">When one of the required dependencies is <see langword="null"/>.</exception>
    internal RunRecordingAgent(
        AIAgent innerAgent,
        IRunStore runStore,
        ITenantContext tenantContext,
        TraconRunRecordingOptions options,
        ILogger<RunRecordingAgent> logger,
        TraconMetrics? metrics = null,
        RunTraceCollector? traceCollector = null,
        string? modelId = null,
        string? modelProvider = null,
        TimeProvider? timeProvider = null,
        TraconAgentGraphOptions? graphOptions = null,
        int? agentVersion = null,
        bool includeAgentVersionTag = true,
        IRunPricingResolver? pricingResolver = null,
        QuotaEnforcer? quotaEnforcer = null,
        IWebhookPublisher? webhookPublisher = null,
        IRunCancellationRegistry? cancellationRegistry = null,
        IRunErrorClassifier? errorClassifier = null,
        IRunInputStore? runInputStore = null,
        RunSampler? runSampler = null,
        ContentGuardPipeline? contentGuardPipeline = null,
        IRunAttributionContext? attributionContext = null,
        IReadOnlyList<IRunEventSink>? sinks = null,
        ToolApprovalPresenterRunner? approvalPresenterRunner = null)
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
        _graphOptions = graphOptions ?? new TraconAgentGraphOptions();
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
        _attributionContext = attributionContext;
        _sinks = sinks is { Count: > 0 } ? sinks : [];
        _approvalPresenterRunner = approvalPresenterRunner;
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
        TraconRunContext.SetCurrent(start.Scope);

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
                ChildRunApproval.CollectRequests(response.Messages) is { Count: > 0 } pendingRequests)
            {
                var presentations = await ResolvePresentationsAsync(pendingRequests, scope, cancellationToken)
                    .ConfigureAwait(false);

                // 🚨 The caller records the pending approval HERE, before the
                // terminal status becomes visible. See the remarks on
                // TraconRunOptions.BeforePendingApprovalIsPublished: closing
                // the run first published a status whose approval was not yet
                // listable, on EVERY queued run that asked for one (F-133).
                await InvokeBeforePendingApprovalAsync(options, response.Messages, presentations, cancellationToken)
                    .ConfigureAwait(false);

                await CompleteAsync(
                    scope,
                    RunStatus.AwaitingApproval,
                    usage,
                    null,
                    cancellationToken,
                    closingEventPayload: BuildAwaitingApprovalPayload(pendingRequests, presentations))
                    .ConfigureAwait(false);

                return response;
            }

            await CompleteAsync(scope, RunStatus.Completed, usage, null, cancellationToken)
                .ConfigureAwait(false);

            return response;
        }
        // 🚨 `when (cancellationSource.IsCancellationRequested)`, not a bare
        // catch. HttpClient reports its OWN request timeout as a
        // TaskCanceledException, so every provider SDK can raise one while
        // NOTHING was cancelled. A bare catch recorded such a run as Canceled
        // with a null error - and a cancelled run is not a failure, so the
        // endpoint answered 200 with an empty body and the operator saw no
        // outage at all. Measured in Phase 157. Anything not cancelled by this
        // run's own source falls through to the failure path below and is
        // classified there.
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
            await CompleteAsync(scope, RunStatus.Canceled, null, null, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            var correlationId = SafeErrorText.NewCorrelationId();
            _logger.LogError(ex, "Run {RunId} failed. (ref: {CorrelationId})", scope.Writer.RunId, correlationId);

            await CompleteAsync(scope, RunStatus.Failed, null, ToRunError(ex, correlationId), CancellationToken.None)
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

        TraconRunContext.SetCurrent(start.Scope);

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

        // The buffered path hands the hook `response.Messages`; the streaming path
        // has no message list, so the approval-request contents are collected as
        // they pass and handed over as ONE assistant message.
        List<AIContent>? approvalContents = null;

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
            //
            // 🚨 HATA-S4-003: it needs its OWN catch as well. The catch below covers only
            // MoveNextAsync inside the loop, so a failure here fell through to the finally,
            // which writes Canceled — and a run that failed is not a run the caller gave up
            // on. The non-streaming path needs no such block: there, one try wraps the whole
            // body and its catch already sees this step.
            try
            {
                await WriteRunStartAsync(start, messages, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Left to the finally, which writes Canceled: that IS the truthful
                // status here and HATA-S4-012 is the case that proves it.
                throw;
            }
            catch (Exception ex)
            {
                completedByCatch = true;
                var startCorrelationId = SafeErrorText.NewCorrelationId();
                _logger.LogError(
                    ex, "Run {RunId} failed before it started. (ref: {CorrelationId})",
                    scope.Writer.RunId, startCorrelationId);

                await CompleteAsync(
                    scope, RunStatus.Failed, null, ToRunError(ex, startCorrelationId), CancellationToken.None)
                    .ConfigureAwait(false);
                throw;
            }

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
                    TraconRunContext.SetCurrent(start.Scope);

                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        naturalEnd = true;
                        break;
                    }

                    update = enumerator.Current;
                }
                // Same filter, same reason as RunCoreAsync above: an
                // OperationCanceledException raised while nothing was
                // cancelled is a provider failure, not a cancelled run.
                catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
                {
                    completedByCatch = true;
                    await CompleteAsync(scope, RunStatus.Canceled, null, null, CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex)
                {
                    completedByCatch = true;
                    var correlationId = SafeErrorText.NewCorrelationId();
                    _logger.LogError(ex, "Run {RunId} failed. (ref: {CorrelationId})", scope.Writer.RunId, correlationId);

                    await CompleteAsync(scope, RunStatus.Failed, null, ToRunError(ex, correlationId), CancellationToken.None)
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

                if (isTopLevelRun && ChildRunApproval.Describe(update.Contents) is not null)
                {
                    topLevelPendingApproval = true;
                    (approvalContents ??= []).AddRange(update.Contents);
                }

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

        string? awaitingApprovalPayload = null;

        if (streamingStatus == RunStatus.AwaitingApproval)
        {
            var approvalMessages = new[] { new ChatMessage(ChatRole.Assistant, approvalContents ?? []) };
            var pendingRequests = ChildRunApproval.CollectRequests(approvalMessages);
            var presentations = await ResolvePresentationsAsync(pendingRequests, scope, cancellationToken)
                .ConfigureAwait(false);

            awaitingApprovalPayload = BuildAwaitingApprovalPayload(pendingRequests, presentations);

            await InvokeBeforePendingApprovalAsync(options, approvalMessages, presentations, cancellationToken)
                .ConfigureAwait(false);
        }

        await CompleteAsync(
            scope,
            streamingStatus,
            ToRunUsage(usage),
            pendingApproval is null ? null : ApprovalError(pendingApproval),
            cancellationToken,
            closingEventPayload: awaitingApprovalPayload).ConfigureAwait(false);
    }
}
