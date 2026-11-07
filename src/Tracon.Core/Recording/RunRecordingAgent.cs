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

    private readonly RunRecordingServices _services;
    private readonly RecordedAgentIdentity _identity;
    private readonly ILogger<RunRecordingAgent> _logger;

    /// <summary>Creates a new recording wrapper.</summary>
    /// <param name="innerAgent">The wrapped agent.</param>
    /// <param name="services">
    /// The services and settings every recorder of a run shares. A member of it that is <see langword="null"/>
    /// switches its feature off: no quota counting, no webhook, no cancellation from outside, no
    /// online evaluation sampling, and so on. See <see cref="RunRecordingServices"/>.
    /// </param>
    /// <param name="identity">
    /// The model and the definition version the catalog knows for the agent.
    /// <see cref="RecordedAgentIdentity.Unknown"/> when none is known.
    /// </param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">When one of the arguments is <see langword="null"/>.</exception>
    internal RunRecordingAgent(
        AIAgent innerAgent,
        RunRecordingServices services,
        RecordedAgentIdentity identity,
        ILogger<RunRecordingAgent> logger)
        : base(innerAgent)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(logger);

        _services = services;
        _identity = identity;
        _logger = logger;
    }

    /// <summary>
    /// Gets the definition version this wrapper records on the run rows it opens, unless a run's own
    /// <see cref="TraconRunOptions.AgentVersion"/> overrides it. <see langword="null"/> when unknown.
    /// </summary>
    /// <remarks>The loop's reviewer trail reads it, so it names the version the reviewer's run recorded.</remarks>
    internal int? AgentVersion => _identity.Version;

    /// <inheritdoc />
    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Before the run starts: options that change what the definition owns are a
        // caller's mistake, not a failed run.
        RunChatOptionsRule.Check(options);

        if (!_services.Recording.Enabled)
        {
            return await base.RunCoreAsync(messages, session, ForInnerAgent(options), cancellationToken).ConfigureAwait(false);
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
        using var cancellationRegistration = _services.CancellationRegistry?.Register(
            start.Scope.RunId,
            start.Scope.RootRunId,
            start.Scope.TenantId,
            cancellationSource);

        var scope = CreateScope(start);

        try
        {
            await WriteRunStartAsync(start, messages, cancellationToken).ConfigureAwait(false);

            // Calls an earlier run of the session left without a result (the loop stopped at a tool that
            // asked for user input) get theirs before anything else, whoever starts this run: a provider
            // refuses a history that holds a call with none.
            var response = await base.RunCoreAsync(
                WithClosedSkippedCalls(messages, session),
                session,
                ForInnerAgent(options),
                cancellationSource.Token).ConfigureAwait(false);

            foreach (var message in response.Messages)
            {
                // A fresh tally for each message: the message is the unit a citation is written once in.
                await WriteContentsAsync(scope, message.Contents, new CitationTally(), message.MessageId, message.Role, cancellationToken)
                    .ConfigureAwait(false);
            }

            await scope.Writer.AppendAsync(
                new RunEventDraft(RunEventType.MessageCompleted) { Text = response.Text },
                cancellationToken).ConfigureAwait(false);

            var usage = ToRunUsage(response.Usage);

            // When a child run ends by requesting approval, the record must not look
            // successful: the model returned a question that cannot be answered, not a result.
            if (FailsOnApproval(start.Scope) && ChildRunApproval.Describe(response.Messages) is { } pending)
            {
                await CompleteAsync(scope, RunStatus.Failed, usage, ApprovalError(pending), cancellationToken)
                    .ConfigureAwait(false);

                return response;
            }

            // When a root run (Depth == 0) ends by requesting approval, it closes with
            // AwaitingApproval instead of Completed — this is INDEPENDENT of the path
            // (management API, OpenAI-compatible endpoint, MCP, A2A) and of whether the run
            // came from the queue (the earlier behavior marked only
            // the queue path, the synchronous paths always said Completed - the approval
            // flag was HIDDEN COMPLETELY). However the decision is DELIVERED
            // (POST /api/approvals/{id}/decide for the queue, or the next turn of the
            // synchronous caller), the status label follows the same principle: an answered
            // run STAYS in this state (the same principle as RunStatus.AwaitingInput).
            // Writing a row into the `pending_approvals` store is a separate decision and
            // DID NOT CHANGE - only the queue path writes it (the queued-run,
            // approval-resume and continuation handlers, through QueuedTurn);
            // this change does not reopen the risk of a double decision race.
            if (WaitsForApproval(start.Scope) &&
                ChildRunApproval.CollectRequests(response.Messages) is { Count: > 0 } pendingRequests)
            {
                var presentations = await ResolvePresentationsAsync(pendingRequests, scope, cancellationToken)
                    .ConfigureAwait(false);

                // 🚨 The caller records the pending approval HERE, before the
                // terminal status becomes visible. See the remarks on
                // TraconRunOptions.BeforePendingApprovalIsPublished: closing
                // the run first published a status whose approval was not yet
                // listable, on EVERY queued run that asked for one.
                await InvokeBeforePendingApprovalAsync(options, response.Messages, presentations, cancellationToken)
                    .ConfigureAwait(false);

                // A turn that waits on an approval AND on a tool's input announces both; the
                // status stays AwaitingApproval.
                await PublishPendingToolInputAsync(session, options, PendingToolInputs(start.Scope), SkippedCalls(response.Messages), cancellationToken)
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

            // A tool's MCP server asked the user for input and the call was suspended:
            // the run neither finished nor failed. The requests come from the ledger the tool wrapper
            // wrote, not from the tool results — a result that merely looks like a request is not one.
            if (PendingToolInputs(start.Scope) is { Count: > 0 } pendingInputs)
            {
                await PublishPendingToolInputAsync(session, options, pendingInputs, SkippedCalls(response.Messages), cancellationToken)
                    .ConfigureAwait(false);

                await CompleteAsync(
                    scope,
                    RunStatus.AwaitingInput,
                    usage,
                    null,
                    cancellationToken,
                    closingEventPayload: BuildAwaitingToolInputPayload(pendingInputs))
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
        // outage at all. Anything not cancelled by this
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
        // The same check as RunCoreAsync; it throws on the first MoveNextAsync.
        RunChatOptionsRule.Check(options);

        if (!_services.Recording.Enabled)
        {
            await foreach (var passthrough in base.RunCoreStreamingAsync(messages, session, ForInnerAgent(options), cancellationToken).ConfigureAwait(false))
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
        using var cancellationRegistration = _services.CancellationRegistry?.Register(
            start.Scope.RunId,
            start.Scope.RootRunId,
            start.Scope.TenantId,
            cancellationSource);

        var scope = CreateScope(start);
        UsageDetails? usage = null;
        string? pendingApproval = null;

        // On a root run, a tool call that requests approval maps to AwaitingApproval
        // instead of Completed; the reason is in the SAME note inside RunCoreAsync
        // (the path/queue distinction was removed). A workflow
        // step whose workflow waits for the answer closes the same way.
        var waitsForApproval = WaitsForApproval(start.Scope);
        var pendingApprovalWaits = false;

        // The buffered path hands the hook `response.Messages`; the streaming path
        // has no message list, so the approval-request contents are collected as
        // they pass and handed over as ONE assistant message.
        List<AIContent>? approvalContents = null;

        // The calls of this run that get no result: see SkippedCallTracker.
        var skippedCalls = new SkippedCallTracker();

        // One tally for the whole stream: it starts a new message itself when an update names another.
        var citations = new CitationTally();

        var enumerator = base.RunCoreStreamingAsync(
                WithClosedSkippedCalls(messages, session),
                session,
                ForInnerAgent(options),
                cancellationSource.Token)
            .GetAsyncEnumerator(cancellationSource.Token);

        // These two flags separate the case where the consumer calls
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
            // This step is DELIBERATELY INSIDE the try/finally — the reason
            // is in the documentation of WriteRunStartAsync and in the note of CreateScope.
            //
            // It needs its OWN catch as well. The catch below covers only
            // MoveNextAsync inside the loop, so a failure here fell through to the finally,
            // which writes Canceled — and a run that failed is not a run the caller gave up
            // on. The non-streaming path needs no such block: there, one try wraps the whole
            // body and its catch already sees this step.
            try
            {
                await WriteRunStartAsync(start, messages, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
            {
                // Left to the finally, which writes Canceled: that IS the truthful
                // status here and an acceptance case proves it. Only OUR
                // cancellation: a store's own timeout (TaskCanceledException) is a
                // failure and takes the catch below.
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
                    // a clean context. Measured: on a streaming run the child
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
                    skippedCalls.Observe(content);

                    if (content is UsageContent usageContent)
                    {
                        // 🚨 SUMMED, not overwritten. A stream carries one
                        // UsageContent per MODEL CALL — every provider adapter Tracon
                        // ships emits it once, at the end of its call — so a run with
                        // a tool round or a loop iteration has several. Keeping the
                        // last one recorded only the final call: a three-turn loop
                        // recorded a third of its tokens, cost and quota. Summing is
                        // what MEAI's own ToChatResponse does. A fresh accumulator,
                        // so no UsageDetails owned by an inner client is modified.
                        (usage ??= new UsageDetails()).Add(usageContent.Details);
                    }
                }

                pendingApproval ??= FailsOnApproval(start.Scope)
                    ? ChildRunApproval.Describe(update.Contents)
                    : null;

                if (waitsForApproval && ChildRunApproval.Describe(update.Contents) is not null)
                {
                    pendingApprovalWaits = true;
                    (approvalContents ??= []).AddRange(update.Contents);
                }

                await WriteContentsAsync(scope, update.Contents, citations, update.MessageId, update.Role, cancellationToken)
                    .ConfigureAwait(false);

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

        // 🚨 The closing steps run after the last frame left, outside the try above, with the CALLER's token. A caller
        // that goes away in that window (a client disconnect at the end of the stream) cancels that token, the store
        // refuses the closing write, and the run stayed Running with its span open: the buffered path closes such a
        // run Canceled in its catch, and so does this one now.
        try
        {
            var pendingInputs = PendingToolInputs(start.Scope);

            var streamingStatus = pendingApproval is not null
                ? RunStatus.Failed
                : pendingApprovalWaits
                    ? RunStatus.AwaitingApproval
                    : pendingInputs.Count > 0
                        ? RunStatus.AwaitingInput
                        : RunStatus.Completed;

            string? closingPayload = null;

            if (streamingStatus == RunStatus.AwaitingApproval)
            {
                var approvalMessages = new[] { new ChatMessage(ChatRole.Assistant, approvalContents ?? []) };
                var pendingRequests = ChildRunApproval.CollectRequests(approvalMessages);
                var presentations = await ResolvePresentationsAsync(pendingRequests, scope, cancellationToken)
                    .ConfigureAwait(false);

                closingPayload = BuildAwaitingApprovalPayload(pendingRequests, presentations);

                await InvokeBeforePendingApprovalAsync(options, approvalMessages, presentations, cancellationToken)
                    .ConfigureAwait(false);

                // The same rule as the buffered path: both are announced, the status stays AwaitingApproval.
                await PublishPendingToolInputAsync(session, options, pendingInputs, skippedCalls.Skipped(), cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (streamingStatus == RunStatus.AwaitingInput)
            {
                closingPayload = BuildAwaitingToolInputPayload(pendingInputs);

                await PublishPendingToolInputAsync(session, options, pendingInputs, skippedCalls.Skipped(), cancellationToken)
                    .ConfigureAwait(false);
            }

            await CompleteAsync(
                scope,
                streamingStatus,
                ToRunUsage(usage),
                pendingApproval is null ? null : ApprovalError(pendingApproval),
                cancellationToken,
                closingEventPayload: closingPayload).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
            await CompleteAsync(scope, RunStatus.Canceled, ToRunUsage(usage), null, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }
}
