using System.Runtime.CompilerServices;
using Google.GenAI;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Gives an HTTP error answer from Gemini the status it carries, in a form the model pipeline reads.
/// </summary>
/// <remarks>
/// <para>
/// The failure of a buffered call and the failure that ends a stream are translated the same way
/// (<see cref="GoogleApiFailure"/>). An error that is not the Google client's own, a cancellation and an error the SDK raised
/// without a status pass through as the same instance. The decorator keeps no state, so it is safe to share across calls.
/// </para>
/// <para>
/// The status is what the fallback chain, the circuit breaker and <see cref="ProviderInvocationException"/> decide from:
/// without it a <c>503</c> was no reason to move to the next model, and a <c>400</c> was counted against the provider.
/// </para>
/// </remarks>
internal sealed class GoogleStatusChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }
        catch (ApiException exception) when (GoogleApiFailure.HasStatus(exception))
        {
            throw GoogleApiFailure.Translate(exception);
        }
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var enumerator = base.GetStreamingResponseAsync(messages, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        await using (enumerator.ConfigureAwait(false))
        {
            while (true)
            {
                bool hasNext;

                // A yield cannot sit inside a try block that has a catch clause: the step is taken here and the
                // update is yielded after it.
                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (ApiException exception) when (GoogleApiFailure.HasStatus(exception))
                {
                    throw GoogleApiFailure.Translate(exception);
                }

                if (!hasNext)
                {
                    yield break;
                }

                yield return enumerator.Current;
            }
        }
    }
}
