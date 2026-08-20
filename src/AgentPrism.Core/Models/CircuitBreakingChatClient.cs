using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Wraps an <see cref="IChatClient"/> with <see cref="ModelProviderCircuitBreaker"/>.
/// </summary>
/// <remarks>
/// <para>
/// It is not embedded in a provider implementation such as <c>AgentPrism.OpenAI</c>.
/// <see cref="ModelProviderRegistry.CreateChatClient"/> wraps every client in this type.
/// Every provider, including current OpenAI and future Anthropic or Gemini, gets the
/// same protection.
/// </para>
/// <para>
/// <strong>Content blocking is not a failure</strong>. An
/// <see cref="AgentPrismContentBlockedException"/> shows that the provider is healthy:
/// the request was blocked locally and never reached the network. Counting it would
/// open the provider circuit after several blocked requests and turn a policy decision
/// into an outage. The same rationale keeps <c>ContentFilterDetectingChatClient</c>
/// outside the circuit breaker. The guard is inside the loop, so it is handled here.
/// </para>
/// </remarks>
internal sealed class CircuitBreakingChatClient(string providerName, IChatClient inner, ModelProviderCircuitBreaker breaker)
    : DelegatingChatClient(inner)
{
    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        breaker.EnsureRequestAllowed(providerName);

        try
        {
            var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            breaker.RecordSuccess(providerName);
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller cancellation says nothing about provider health.
            throw;
        }
        catch (AgentPrismContentBlockedException)
        {
            // The content guard decision says nothing either. The request did not reach the network.
            throw;
        }
        catch (Exception)
        {
            breaker.RecordFailure(providerName);
            throw;
        }
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        breaker.EnsureRequestAllowed(providerName);

        var enumerator = base.GetStreamingResponseAsync(messages, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        await using (enumerator.ConfigureAwait(false))
        {
            while (true)
            {
                var hasNext = false;

                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (AgentPrismContentBlockedException)
                {
                    throw;
                }
                catch (Exception)
                {
                    breaker.RecordFailure(providerName);
                    throw;
                }

                if (!hasNext)
                {
                    break;
                }

                yield return enumerator.Current;
            }

            breaker.RecordSuccess(providerName);
        }
    }
}
