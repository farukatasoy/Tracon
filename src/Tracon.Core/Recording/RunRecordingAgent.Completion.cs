using System.Diagnostics;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Run completion: usage/cost merging, error mapping, and the terminal store
/// write. <see cref="CompleteAsync"/> fans out to the root-only notifications
/// in <c>RunRecordingAgent.Notifications.cs</c>, but owns the outcome itself.
/// </summary>
public sealed partial class RunRecordingAgent
{
    /// <summary>
    /// Reports whether a run that ends by requesting approval closes
    /// <see cref="RunStatus.AwaitingApproval"/>: a root run, and a workflow step whose workflow waits for
    /// the answer.
    /// </summary>
    private static bool WaitsForApproval(AgentRunScope scope)
        => scope.Depth == 0 || scope.SuspendsWorkflowOnApproval;

    /// <summary>
    /// Reports whether a run that ends by requesting approval is <see cref="RunStatus.Failed"/>: a sub-run
    /// of an agent tree cannot wait in the middle of the tree.
    /// </summary>
    private static bool FailsOnApproval(AgentRunScope scope) => !WaitsForApproval(scope);

    private static RunError ApprovalError(string toolNames)
        => new()
        {
            Type = nameof(TraconException),
            Message = $"The child run requested user approval for the '{toolNames}' tool. " +
                      "A child agent cannot request approval: approval is the input of the next " +
                      "turn and cannot be awaited in the middle of the call tree. Define an " +
                      "automatic approval rule for this tool, or limit the child agent to tools " +
                      "that need no approval.",
        };

    /// <summary>
    /// Lets the caller finish its own bookkeeping before the terminal
    /// <see cref="RunStatus.AwaitingApproval"/> status is published.
    /// </summary>
    private static async ValueTask InvokeBeforePendingApprovalAsync(
        AgentRunOptions? options,
        IEnumerable<ChatMessage> messages,
        IReadOnlyDictionary<string, ToolApprovalPresentation?> presentations,
        CancellationToken cancellationToken)
    {
        if (options is TraconRunOptions { BeforePendingApprovalIsPublished: { } hook })
        {
            await hook(messages, presentations, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Resolves a presentation for every pending request, or an empty result when no presenter is registered.</summary>
    private async ValueTask<IReadOnlyDictionary<string, ToolApprovalPresentation?>> ResolvePresentationsAsync(
        IReadOnlyList<ToolApprovalRequestContent> requests,
        RunScope scope,
        CancellationToken cancellationToken)
        => _approvalPresenterRunner is null
            ? EmptyPresentations
            : await _approvalPresenterRunner
                .ResolveAllAsync(requests, scope.TenantId, scope.AgentName, cancellationToken)
                .ConfigureAwait(false);

    private static readonly IReadOnlyDictionary<string, ToolApprovalPresentation?> EmptyPresentations =
        new Dictionary<string, ToolApprovalPresentation?>(StringComparer.Ordinal);

    /// <summary>Builds the <see cref="RunEventType.RunAwaitingInput"/> closing event's <c>Payload</c>.</summary>
    private static string BuildAwaitingApprovalPayload(
        IReadOnlyList<ToolApprovalRequestContent> requests,
        IReadOnlyDictionary<string, ToolApprovalPresentation?> presentations)
    {
        var items = new List<PendingToolApprovalEventItem>(requests.Count);

        foreach (var request in requests)
        {
            var presentation = presentations.GetValueOrDefault(request.RequestId);

            items.Add(new PendingToolApprovalEventItem
            {
                RequestId = request.RequestId,
                ToolName = request.ToolCall is FunctionCallContent call ? call.Name : request.ToolCall.CallId,
                EntityType = presentation?.EntityType,
                EntityId = presentation?.EntityId,
                EntityName = presentation?.EntityName,
                Message = presentation?.Message,
            });
        }

        return JsonSerializer.Serialize(
            (IReadOnlyList<PendingToolApprovalEventItem>)items,
            TraconCoreJsonContext.Default.IReadOnlyListPendingToolApprovalEventItem);
    }

    /// <summary>
    /// Reads the tool input requests that suspended a call of the run the scope belongs to.
    /// </summary>
    /// <returns>The requests; empty when no call was suspended.</returns>
    private static IReadOnlyList<PendingToolInput> PendingToolInputs(AgentRunScope scope)
        => scope.ToolInputs is { HasPending: true } ledger ? ledger.Snapshot() : [];

    /// <summary>
    /// Records the pending tool input requests with the session and announces them to the caller, both
    /// before the terminal status is published.
    /// </summary>
    /// <remarks>
    /// Runs on BOTH the buffered and the streaming path, and when the run ends by waiting for approval
    /// as well: a turn that waits on both announces both (the status stays
    /// <see cref="RunStatus.AwaitingApproval"/>). Anything either step throws fails the run, as for approvals:
    /// a request that could not be recorded is one nobody can answer.
    /// </remarks>
    private static async ValueTask PublishPendingToolInputAsync(
        AgentSession? session,
        AgentRunOptions? options,
        IReadOnlyList<PendingToolInput> pending,
        IReadOnlyList<string> skippedCalls,
        CancellationToken cancellationToken)
    {
        if (pending.Count == 0)
        {
            return;
        }

        ToolInputSessionBridge.Record(session, pending, skippedCalls);

        if (options is TraconRunOptions { BeforePendingToolInputIsPublished: { } hook })
        {
            await hook(pending, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Puts the results of the calls an earlier run of the session left without one in front of the run's messages.
    /// </summary>
    private static IEnumerable<ChatMessage> WithClosedSkippedCalls(IEnumerable<ChatMessage> messages, AgentSession? session)
        => ToolInputSessionBridge.CloseSkippedCalls(session) is { } closing ? [closing, .. messages] : messages;

    /// <summary>Reads the calls of a finished run that never got a result.</summary>
    private static IReadOnlyList<string> SkippedCalls(IEnumerable<ChatMessage> produced)
    {
        var tracker = new SkippedCallTracker();
        tracker.Observe(produced);

        return tracker.Skipped();
    }

    /// <summary>Builds the <see cref="RunEventType.RunAwaitingInput"/> closing event's <c>Payload</c> for tool input.</summary>
    private static string BuildAwaitingToolInputPayload(IReadOnlyList<PendingToolInput> pending)
        => JsonSerializer.Serialize(
            (IReadOnlyList<PendingToolInputEventItem>)[.. pending.Select(static input => new PendingToolInputEventItem
            {
                RequestId = input.RequestId,
                ToolName = input.ToolName,
                ServerName = input.ServerName,
            })],
            TraconCoreJsonContext.Default.IReadOnlyListPendingToolInputEventItem);

    private async ValueTask CompleteAsync(
        RunScope scope,
        RunStatus status,
        RunUsage? usage,
        RunError? error,
        CancellationToken cancellationToken,
        string? closingEventPayload = null)
    {
        // 🚨 The classifier is called ONLY when there is an error: on a successful run
        // (error is null) it never fires on the hot path.
        if (error is not null && _errorClassifier is not null)
        {
            var classification = ClassifyOrFallback(error);
            error = error with { Class = classification.Class, Fingerprint = classification.Fingerprint };
        }

        // A run that ended without an answer has no messages to read its tool calls from, so the calls the tool
        // chain ran are written here, from the run's ledger, and the loop below closes the ones that never ended.
        // A run that answered wrote its rows from its messages (timed from the ledger, which they claim). A call that
        // only the ledger knows then is one that an earlier model link ran and a later link had answered again from
        // its result: a second row for it would read as a second execution, so a run that answered writes none.
        if (status is RunStatus.Failed or RunStatus.Canceled)
        {
            await WriteLedgerCallsAsync(scope, cancellationToken).ConfigureAwait(false);
        }

        // Calls that end before a result arrives are closed explicitly; otherwise a tool
        // card that looks "started but not finished" would stay in the user interface.
        foreach (var unfinished in scope.Tools.DrainUnfinished("The run ended before the tool result arrived."))
        {
            await scope.Writer.RecordToolInvocationAsync(unfinished, cancellationToken).ConfigureAwait(false);
            _metrics?.RecordToolInvocation(unfinished.ToolName, succeeded: false, unfinished.Duration);
        }

        // What the run's tools measured beyond tokens is read ONCE, here, after the
        // unfinished calls above gave up the measurements they had reported. It is a
        // snapshot like the cost: an item reported later stays in the detail only.
        var nonTokenCost = scope.NonTokenCost?.Seal();

        // The tokens a side-channel call spends (the compaction summarizer, the
        // aiJudge loop judge, a discarded repair turn, the turns of a loop that
        // failed its run) never reach the AgentResponse; if they are not added here,
        // the run's usage, cost and quota entry stay incomplete. 🚨 They are kept
        // apart by model (F-349): the agent's own model share joins the agent's
        // usage, every other model's share is priced at that model below. The run's
        // usage is the sum of all of them.
        var agentUsage = RunUsageSum.Add(usage, scope.ExtraUsage?.ToAgentModelUsage());
        var otherModels = scope.ExtraUsage?.ToOtherModelUsages() ?? [];
        usage = agentUsage;

        foreach (var share in otherModels)
        {
            usage = RunUsageSum.Add(usage, share.Usage);
        }

        // 🚨 A ModelBinding.Fallbacks link may have answered instead of the
        // primary binding (phase 62). scope.FallbackAttribution is written by
        // FallbackChatClient through the ambient TraconRunContext, the
        // same pattern AgentRunScope.ToolUsage uses (phase 28) — the model
        // that actually ran is not known until the call already happened,
        // long after this instance's own _modelId/_modelProvider were fixed
        // at agent-compile time. Every downstream consumer of "the model"
        // below uses this override so cost, metrics, and runs.model_id all
        // agree with what really ran, not with the primary binding.
        var fallbackUsed = scope.FallbackAttribution?.Current;
        var modelProvider = fallbackUsed?.Provider ?? _modelProvider;
        var modelId = fallbackUsed?.Model ?? _modelId;

        // The cost is calculated HERE, once, when the run ends — the price is a
        // snapshot (see Phase 20 section 20.2): if the price
        // list changes later, the cost of this run does not change. Each model's
        // share is priced at that model; with no other model it is exactly the
        // resolver's answer for the agent's model (RunCostCombiner).
        var cost = _pricingResolver is null
            ? null
            : RunCostCombiner.Resolve(_pricingResolver, modelProvider, modelId, agentUsage, otherModels);

        // 🚨 Quota accounting runs BEFORE the terminal write (phase 146,
        // 146.1). Consumption is real the moment it is spent — the tokens
        // are already billed by the provider regardless of how this run's
        // row closes — and a threshold notice appended AFTER the terminal
        // event would never land inside this run's own append-only stream:
        // by the time a reader sees the terminal event, it has stopped
        // reading. RecordQuotaAsync itself never throws
        // (QuotaEnforcer.RecordAsync's contract) so this cannot fail the run.
        // F-418: EVERY run in the tree writes its own consumption, when it ends,
        // under the root's tenant and name. The root used to write the whole tree
        // once, from the run store, while its own row closed; a background child
        // that had not ended by then had no usage on its row and was never counted.
        var own = OwnConsumption(usage, cost, nonTokenCost);

        await RecordQuotaAsync(scope, own, cost, nonTokenCost, cancellationToken).ConfigureAwait(false);

        // The run.* webhook is the other reader of the call tree (F-383): a root
        // publishes the tree as it stands when the root closes. Read once here,
        // before the root's row closes.
        var treeConsumption = scope.Depth == 0
            ? await ReadTreeConsumptionAsync(scope, own, cancellationToken).ConfigureAwait(false)
            : default;

        await scope.Writer.CompleteAsync(
            status,
            usage,
            error,
            cost,
            nonTokenCost: nonTokenCost,

            // null unless a fallback link answered: leaves runs.model_id at
            // the value WriteRunStartAsync already wrote (the overwhelmingly
            // common case), never a redundant write of the same value.
            modelId: fallbackUsed?.Model,
            modelProvider: fallbackUsed?.Provider,
            closingEventPayload: closingEventPayload,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // 🚨 The budget is NOT recorded here (phase 114, was: `scope.Budget?.RecordUsage(...)`).
        // RunBudgetChatClient records every real model call the moment it
        // happens, from INSIDE the tool-call loop - including the calls that
        // make up `usage` above AND the side-channel calls merged into it (the
        // compaction summarizer, the aiJudge loop judge), because every one of
        // them is built through the same ModelProviderRegistry pipeline that ring
        // sits in. Recording the merged total here a second time would double
        // every tree's spend.
        var elapsed = _timeProvider.GetElapsedTime(scope.StartedAt);

        // 🚨 Event publication runs ONLY on a root run. A child run is part of the same
        // user request; a separate run.completed event would be emitted for every node.
        if (scope.Depth == 0)
        {
            await PublishRunEventAsync(scope, status, treeConsumption, error, elapsed, modelId, cancellationToken).ConfigureAwait(false);
            await SampleForOnlineEvalAsync(scope, status, cancellationToken).ConfigureAwait(false);
        }

        _metrics?.RecordRun(
            scope.AgentName,
            status,
            scope.TenantId,
            modelId,
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
                modelId,
                scope.TenantId,
                knownCost.Total() ?? 0m,
                knownCost.Currency ?? "unknown");
        }

        if (scope.Activity is null)
        {
            return;
        }

        scope.Activity.SetTag(TraconDiagnostics.Tags.Status, status.ToString());

        // A fallback link answering instead of the primary binding updates the
        // root span's model tag too — not just the run record — so a trace
        // viewer shows the model that actually ran without cross-referencing
        // the run's events (phase 62).
        if (fallbackUsed is not null)
        {
            scope.Activity.SetTag(TraconDiagnostics.Tags.ModelId, fallbackUsed.Value.Model);
        }

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
                scope.Activity.SpanId.ToString(),
                scope.Writer.RunId,
                scope.TenantId,
                status,
                cancellationToken).ConfigureAwait(false);
        }

        scope.Activity.Dispose();
    }

    /// <summary>
    /// Calls <see cref="_errorClassifier"/>, falling back to Tracon's
    /// built-in classification if it throws.
    /// </summary>
    /// <remarks>
    /// A consumer's <see cref="IRunErrorClassifier"/> is composed into the
    /// completion path, not just registered - a bug in it must not stop the
    /// run from reaching its terminal state. Falls back to
    /// a fresh <see cref="DefaultRunErrorClassifier"/> rather than skipping
    /// classification: <see cref="RunErrorClassification.Fingerprint"/> is
    /// required, and <see cref="RunError.Class"/> staying <see langword="null"/>
    /// would misreport a classified taxonomy as "no classifier ran" to a reader
    /// of <see cref="RunError"/>'s XML doc.
    /// </remarks>
    private RunErrorClassification ClassifyOrFallback(RunError error)
    {
        try
        {
            return _errorClassifier!.Classify(error);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "The registered IRunErrorClassifier threw while classifying a run error; falling back to the built-in classifier.");

            return new DefaultRunErrorClassifier().Classify(error);
        }
    }

    private static RunUsage? ToRunUsage(UsageDetails? usage)
        => usage is null
            ? null
            : new RunUsage
            {
                InputTokens = usage.InputTokenCount,
                OutputTokens = usage.OutputTokenCount,
                TotalTokens = usage.TotalTokenCount,

                // 🚨 These four are counted INSIDE the three totals above (the
                // Microsoft.Extensions.AI contract), so they are recorded beside
                // them, never added to them. A provider that does not report one
                // leaves it null; writing zero would claim a measurement that was
                // never made.
                CachedInputTokens = UsageBreakdown.CachedInputTokens(usage),
                ReasoningTokens = UsageBreakdown.ReasoningTokens(usage),
                AudioInputTokens = UsageBreakdown.AudioInputTokens(usage),
                AudioOutputTokens = UsageBreakdown.AudioOutputTokens(usage),
            };

    // Tracon exceptions can carry their own stable error type name (for example
    // content_filtered). The default value is still the full name of the type, so the shape
    // of the existing records does not change.
    private static RunError ToRunError(Exception exception, string correlationId)
        => new()
        {
            Type = exception is TraconException traconException
                ? traconException.ErrorType
                : exception.GetType().FullName ?? exception.GetType().Name,
            Message = SafeErrorText.ForPersistence(exception, correlationId),
        };
}
