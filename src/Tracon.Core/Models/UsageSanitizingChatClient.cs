using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Drops a token count below zero from what a provider reported, so nothing above this ring can price it.
/// </summary>
/// <remarks>
/// <para>
/// A provider, a proxy in front of one, or a compatible server of someone's own can report <c>-5</c> prompt tokens. The
/// count then travelled as it came: the run record kept it, and a price times a negative count is a negative cost,
/// which a quota, a budget and a cost total read as money handed back. A count is "unknown" or it is a number of
/// tokens; a negative one is not a measurement, so it becomes unknown (<see langword="null"/>), the same state a
/// provider that reports nothing leaves.
/// </para>
/// <para>
/// The ring sits right above the raw client, below the limiter, the guard, the cache and the usage trackers, so each of
/// them sees the same counts. The counts of a response and of a streamed update are changed in place: the provider
/// built them for this call and nothing else holds them.
/// </para>
/// </remarks>
internal sealed class UsageSanitizingChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    /// <summary>Replaces every token count below zero in <paramref name="usage"/> with <see langword="null"/> or removes it.</summary>
    /// <param name="usage">The usage a provider reported; <see langword="null"/> is left alone.</param>
    internal static void Sanitize(UsageDetails? usage)
    {
        if (usage is null)
        {
            return;
        }

        usage.InputTokenCount = NonNegative(usage.InputTokenCount);
        usage.OutputTokenCount = NonNegative(usage.OutputTokenCount);
        usage.TotalTokenCount = NonNegative(usage.TotalTokenCount);
        usage.CachedInputTokenCount = NonNegative(usage.CachedInputTokenCount);
        usage.ReasoningTokenCount = NonNegative(usage.ReasoningTokenCount);

        if (usage.AdditionalCounts is { Count: > 0 } additional)
        {
            foreach (var name in additional.Where(static pair => pair.Value < 0).Select(static pair => pair.Key).ToList())
            {
                additional.Remove(name);
            }
        }
    }

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

        Sanitize(response.Usage);

        return response;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            foreach (var content in update.Contents)
            {
                if (content is UsageContent usage)
                {
                    Sanitize(usage.Details);
                }
            }

            yield return update;
        }
    }

    private static long? NonNegative(long? count) => count < 0 ? null : count;
}
