using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Counts the thinking tokens of a Gemini call inside its output tokens, as the other providers report them.
/// </summary>
/// <remarks>
/// <para>
/// Gemini reports the answer in <c>candidatesTokenCount</c> and the thinking in <c>thoughtsTokenCount</c>, two counts that
/// do not overlap, and it bills the thinking at the output price. The adapter copies them as they are: the output count
/// holds the answer and the reasoning count holds the thinking. OpenAI and Anthropic report the reasoning count as a part
/// of the output count, and everything downstream reads it that way: the cost is the output count at the output price, and
/// the budget, the quota, the metrics and the run screen take the reasoning count to be at most the output count. A call
/// with 26 answer tokens and 319 thinking tokens was priced as 26.
/// </para>
/// <para>
/// The thinking is added to the output count here, once per call of the model. The reasoning count and the total are left as
/// the service reported them (the total already holds both). Sits directly on the adapter, below the tool loop, so every
/// round of a tool conversation is corrected once and no layer above sees the uncorrected count.
/// </para>
/// </remarks>
internal sealed class GoogleUsageChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

        CountThinkingAsOutput(response.Usage);

        return response;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            foreach (var content in update.Contents)
            {
                if (content is UsageContent usage)
                {
                    CountThinkingAsOutput(usage.Details);
                }
            }

            yield return update;
        }
    }

    private static void CountThinkingAsOutput(UsageDetails? usage)
    {
        if (usage is { ReasoningTokenCount: > 0 } counted)
        {
            counted.OutputTokenCount = (counted.OutputTokenCount ?? 0) + counted.ReasoningTokenCount;
        }
    }
}
