using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Wraps the <see cref="IChatClient"/> of a model call that a run makes outside
/// the agent's own turns, and adds the call's token usage to the run under the
/// model that spent it.
/// </summary>
/// <remarks>
/// <para>
/// Two callers use it: the summarization call of context compaction
/// (<see cref="SummarizationCompactionStrategy"/>) and the judge of an
/// <c>aiJudge</c> loop criterion (<c>AIJudgeLoopEvaluator</c>). Both run on a
/// model binding of their own and are fully separate from the agent's
/// <c>AgentResponse</c>; MAF drops the judge's response entirely. Without this
/// wrapper their tokens reach the tree budget (<see cref="RunBudgetChatClient"/>
/// sits in their pipeline) but not the run's usage, cost or quota entry.
/// </para>
/// <para>
/// The usage goes to <see cref="SideChannelUsageAccumulator"/> with the
/// provider and model that answered, so the run prices it at THAT model.
/// That is the binding's own model unless one of the binding's
/// <see cref="ModelBinding.Fallbacks"/> answered instead.
/// Both paths sum every <see cref="UsageContent"/> of a call: a stream can
/// report usage in more than one part (the same rule as the run recorder).
/// </para>
/// <para>
/// <strong>A side channel's fallback is not the run's.</strong> For
/// the duration of the call the wrapper puts a copy of the run's scope in
/// place, with <see cref="AgentRunScope.SideChannelFallback"/> set, and
/// <c>FallbackChatClient</c> writes the link that answered there. The run's
/// model, its fallback event and the agent's price stay with the agent's own
/// binding. The copy is written in this class's own method bodies, and again
/// before every step of the streaming loop: an ambient write does not cross a
/// <c>yield return</c> (TRC0501).
/// </para>
/// <para>
/// The judge call also names the model that answered to the loop criterion being evaluated
/// (<see cref="AgentRunScope.CriterionTrail"/>), which is how the iteration event reports the model
/// the call really used.
/// </para>
/// <para>
/// The accumulator is read from <see cref="TraconRunContext"/> once, when the
/// call starts — the wrapper is part of a compiled agent that is cached across
/// runs, so nothing run-specific is captured at construction.
/// </para>
/// <para>
/// Only the compaction call opens a span of its own
/// (<see cref="TraconDiagnostics.CompactHistoryActivityName"/>), and only on the
/// non-streaming path: the summarizer never streams, and a span opened inside
/// an async iterator would not flow back to its caller.
/// Precedent: <see cref="CircuitBreakingChatClient"/>.
/// </para>
/// </remarks>
internal sealed class SideChannelUsageTrackingChatClient : DelegatingChatClient
{
    private static readonly ActivitySource ActivitySource = new(TraconDiagnostics.ActivitySourceName);

    private readonly string _provider;
    private readonly string _model;
    private readonly bool _tracesCompaction;

    private SideChannelUsageTrackingChatClient(IChatClient inner, ModelBinding binding, bool tracesCompaction)
        : base(inner)
    {
        ArgumentNullException.ThrowIfNull(binding);

        _provider = binding.Provider;
        _model = binding.Model;
        _tracesCompaction = tracesCompaction;
    }

    /// <summary>Wraps the client of the compaction summarizer.</summary>
    /// <param name="inner">The summarizer's client.</param>
    /// <param name="binding">The model binding the summarizer runs on.</param>
    /// <returns>The wrapper. It also opens the <c>compact_history</c> span.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static SideChannelUsageTrackingChatClient ForCompaction(IChatClient inner, ModelBinding binding)
        => new(inner, binding, tracesCompaction: true);

    /// <summary>Wraps the client of an <c>aiJudge</c> loop criterion's judge.</summary>
    /// <param name="inner">The judge's client.</param>
    /// <param name="binding">The model binding the judge runs on.</param>
    /// <returns>The wrapper.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static SideChannelUsageTrackingChatClient ForLoopJudge(IChatClient inner, ModelBinding binding)
        => new(inner, binding, tracesCompaction: false);

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var scope = TraconRunContext.Current;
        var accumulator = scope?.ExtraUsage;
        var answeredBy = new FallbackModelAttribution();

        // In this method's own body: the write flows down into the call below,
        // and an async method never hands it back to its caller.
        if (scope is not null)
        {
            TraconRunContext.SetCurrent(scope with { SideChannelFallback = answeredBy });
        }

        using var activity = _tracesCompaction
            ? ActivitySource.StartActivity(TraconDiagnostics.CompactHistoryActivityName)
            : null;

        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

        var (provider, model) = AnsweringModel(answeredBy);
        accumulator?.Add(response.Usage, provider, model);
        RecordJudge(scope, provider, model);

        if (activity is not null && response.Usage is { } usage)
        {
            activity.SetTag(TraconDiagnostics.Tags.CompactionInputTokens, usage.InputTokenCount);
            activity.SetTag(TraconDiagnostics.Tags.CompactionOutputTokens, usage.OutputTokenCount);
        }

        return response;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var scope = TraconRunContext.Current;
        var accumulator = scope?.ExtraUsage;
        var answeredBy = new FallbackModelAttribution();
        var callScope = scope is null ? null : scope with { SideChannelFallback = answeredBy };
        UsageDetails? usage = null;

        var enumerator = base.GetStreamingResponseAsync(messages, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        try
        {
            while (true)
            {
                // 🚨 Rewritten before EVERY step: an ambient write made in an
                // async iterator does not cross a yield return, and the next
                // MoveNextAsync would run the fallback chain with the run's own
                // scope (RunRecordingAgent.RunCoreStreamingAsync, phase 12).
                if (callScope is not null)
                {
                    TraconRunContext.SetCurrent(callScope);
                }

                if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    break;
                }

                var update = enumerator.Current;

                foreach (var content in update.Contents)
                {
                    if (content is UsageContent usageContent)
                    {
                        // Summed, not overwritten: a fresh accumulator, so no
                        // UsageDetails owned by an inner client is modified.
                        (usage ??= new UsageDetails()).Add(usageContent.Details);
                    }
                }

                yield return update;
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);

            // Also when the caller stops early or the stream fails: the tokens
            // reported so far are already spent.
            var (provider, model) = AnsweringModel(answeredBy);
            accumulator?.Add(usage, provider, model);
            RecordJudge(scope, provider, model);
        }
    }

    /// <summary>
    /// Hands the model that answered a judge call to the criterion being evaluated, so the loop's
    /// audit trail names the model the call really used and not the binding it was configured with.
    /// </summary>
    private void RecordJudge(AgentRunScope? scope, string provider, string model)
    {
        if (!_tracesCompaction)
        {
            scope?.CriterionTrail?.RecordJudge(provider, model);
        }
    }

    /// <summary>The model that answered the call: a fallback link when one did, otherwise the binding's own.</summary>
    private (string Provider, string Model) AnsweringModel(FallbackModelAttribution answeredBy)
        => answeredBy.Current ?? (_provider, _model);
}
