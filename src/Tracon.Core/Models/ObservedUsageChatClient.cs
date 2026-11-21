using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Adds the usage of every response the provider billed to the running run's
/// <see cref="ObservedUsageAccumulator"/>, the moment the response arrives.
/// </summary>
/// <remarks>
/// <para>
/// The ring sits right above the raw client (above <see cref="UsageSanitizingChatClient"/>, so it adds the counts
/// the rest of the pipeline sees), and below the limiter, the guard, the budget, the cache, the finish-reason
/// detector and the tool-call loop. That position is the point: each response that came from the provider is
/// seen exactly once, including a response a ring above rejects (a content-filtered or a truncated answer) and
/// every turn of the tool-call loop. A cache hit never reaches it, and a hit spends nothing.
/// </para>
/// <para>
/// Why it exists: the run recorder reads a run's usage from its answer, and a run that ends in an exception or a
/// cancellation has none. The tokens that run spent before it ended were billed all the same, and the run's row,
/// its cost and its quota entry said it spent nothing.
/// </para>
/// <para>
/// <strong>The agent's own model only.</strong> A call made on behalf of the run through a pipeline of its own (the
/// compaction summarizer, an <c>aiJudge</c> loop judge) goes through this ring too, and runs with
/// <see cref="AgentRunScope.SideChannelFallback"/> set; it is left alone here because
/// <see cref="SideChannelUsageTrackingChatClient"/> counts it under the model that answered it.
/// </para>
/// <para>
/// <strong>Reads <see cref="TraconRunContext"/>, never writes it.</strong> The compiled agent, and so this
/// instance, is cached across runs; the accumulator belongs to the run and is read from the scope once when
/// the call starts. A stream reads it before its first update; the run recorder rewrites the scope before every
/// step of the stream, so the first step finds it.
/// </para>
/// <para>
/// A sub-agent's run opens a scope of its own, with an accumulator of its own, so its calls are never added to
/// the run that called it.
/// </para>
/// </remarks>
internal sealed class ObservedUsageChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var accumulator = AccumulatorOfTheRunningRun();

        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

        accumulator?.Add(response.Usage);

        return response;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var accumulator = AccumulatorOfTheRunningRun();

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            if (accumulator is not null)
            {
                foreach (var content in update.Contents)
                {
                    // Added as each part passes, not when the stream ends: a stream that fails or is
                    // abandoned after its usage part has still been billed for it.
                    if (content is UsageContent usage)
                    {
                        accumulator.Add(usage.Details);
                    }
                }
            }

            yield return update;
        }
    }

    // The agent's own call only: a side-channel call runs with its own marker in the scope.
    private static ObservedUsageAccumulator? AccumulatorOfTheRunningRun()
        => TraconRunContext.Current is { SideChannelFallback: null, ObservedUsage: { } accumulator } ? accumulator : null;
}
