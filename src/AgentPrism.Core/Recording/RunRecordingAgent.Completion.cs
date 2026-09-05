using System.Diagnostics;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Run completion: usage/cost merging, error mapping, and the terminal store
/// write. <see cref="CompleteAsync"/> fans out to the root-only notifications
/// in <c>RunRecordingAgent.Notifications.cs</c>, but owns the outcome itself.
/// </summary>
public sealed partial class RunRecordingAgent
{
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
        if (options is AgentPrismRunOptions { BeforePendingApprovalIsPublished: { } hook })
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
            AgentPrismCoreJsonContext.Default.IReadOnlyListPendingToolApprovalEventItem);
    }

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

        // 🚨 A ModelBinding.Fallbacks link may have answered instead of the
        // primary binding (phase 62). scope.FallbackAttribution is written by
        // FallbackChatClient through the ambient AgentPrismRunContext, the
        // same pattern AgentRunScope.ToolUsage uses (phase 28) — the model
        // that actually ran is not known until the call already happened,
        // long after this instance's own _modelId/_modelProvider were fixed
        // at agent-compile time. Every downstream consumer of "the model"
        // below uses this override so cost, metrics, and runs.model_id all
        // agree with what really ran, not with the primary binding.
        var fallbackUsed = scope.FallbackAttribution?.Current;
        var modelProvider = fallbackUsed?.Provider ?? _modelProvider;
        var modelId = fallbackUsed?.Model ?? _modelId;

        // The cost is calculated HERE, from the final (merged) usage — the price is a
        // snapshot (see docs/arsiv/fazlar/20-MALIYET-VE-GOSTERGE-PANELI.md section 20.2): if the price
        // list changes later, the cost of this run does not change.
        var cost = _pricingResolver?.Resolve(modelProvider, modelId, usage);

        // 🚨 Quota accounting runs BEFORE the terminal write (phase 146,
        // 146.1). Consumption is real the moment it is spent — the tokens
        // are already billed by the provider regardless of how this run's
        // row closes — and a threshold notice appended AFTER the terminal
        // event would never land inside this run's own append-only stream:
        // by the time a reader sees the terminal event, it has stopped
        // reading. Only a root run accounts (the same Depth == 0 gate as
        // the block below); RecordQuotaAsync itself never throws
        // (QuotaEnforcer.RecordAsync's contract) so this cannot fail the run.
        if (scope.Depth == 0)
        {
            await RecordQuotaAsync(scope, usage, cost, cancellationToken).ConfigureAwait(false);
        }

        await scope.Writer.CompleteAsync(
            status,
            usage,
            error,
            cost,

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
        // make up `usage` above AND the compaction side-channel merged into
        // it, because both are built through the same
        // IModelProviderRegistry.CreateChatClient(binding) pipeline that ring
        // sits in. Recording the merged total here a second time would double
        // every tree's spend.
        var elapsed = _timeProvider.GetElapsedTime(scope.StartedAt);

        // 🚨 Event publication runs ONLY on a root run (quota accounting above shares the
        // same gate). A child run is part of the same user request; if it were counted
        // separately, an agent tree would consume the quota as fast as its depth, and a
        // separate run.completed event would be emitted for every node.
        if (scope.Depth == 0)
        {
            await PublishRunEventAsync(scope, status, usage, cost, error, elapsed, modelId, cancellationToken).ConfigureAwait(false);
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

        scope.Activity.SetTag(AgentPrismDiagnostics.Tags.Status, status.ToString());

        // A fallback link answering instead of the primary binding updates the
        // root span's model tag too — not just the run record — so a trace
        // viewer shows the model that actually ran without cross-referencing
        // the run's events (phase 62).
        if (fallbackUsed is not null)
        {
            scope.Activity.SetTag(AgentPrismDiagnostics.Tags.ModelId, fallbackUsed.Value.Model);
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
    /// Calls <see cref="_errorClassifier"/>, falling back to AgentPrism's
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

            // 🚨 The breakdown fields use a NULL-PRESERVING sum, unlike the three
            // totals above. `(a ?? 0) + (b ?? 0)` would turn "neither side
            // measured this" into the claim "measured, and it was zero"; on a run
            // whose provider never reports cache usage that would report a 0%
            // cache hit rate as if it had been observed.
            CachedInputTokens = AddOrNull(primary.CachedInputTokens, extra.CachedInputTokens),
            ReasoningTokens = AddOrNull(primary.ReasoningTokens, extra.ReasoningTokens),
            AudioInputTokens = AddOrNull(primary.AudioInputTokens, extra.AudioInputTokens),
            AudioOutputTokens = AddOrNull(primary.AudioOutputTokens, extra.AudioOutputTokens),
        };
    }

    /// <summary>Adds two optional counters, staying <see langword="null"/> when NEITHER side reported one.</summary>
    private static long? AddOrNull(long? left, long? right)
        => left is null && right is null ? null : (left ?? 0) + (right ?? 0);

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

    // AgentPrism exceptions can carry their own stable error type name (for example
    // content_filtered). The default value is still the full name of the type, so the shape
    // of the existing records does not change.
    private static RunError ToRunError(Exception exception, string correlationId)
        => new()
        {
            Type = exception is AgentPrismException prismException
                ? prismException.ErrorType
                : exception.GetType().FullName ?? exception.GetType().Name,
            Message = SafeErrorText.ForPersistence(exception, correlationId),
        };
}
