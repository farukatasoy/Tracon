using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Wraps an <see cref="IChatClient"/> with <see cref="ModelProviderCircuitBreaker"/>.
/// </summary>
/// <remarks>
/// <para>
/// It is not embedded in a provider implementation such as <c>Tracon.OpenAI</c>.
/// <see cref="ModelProviderRegistry.CreateChatClient"/> wraps every client in this type.
/// Every provider, including current OpenAI and future Anthropic or Gemini, gets the
/// same protection.
/// </para>
/// <para>
/// <strong>Only a failure that says the provider is unhealthy counts</strong>
/// (<see cref="CountsAgainstProvider"/>). The circuit of the setup-time credential
/// is shared by every tenant, so a failure that belongs to one request or one run
/// must not count: one tenant could otherwise open the circuit for all the others.
/// A content block (<see cref="TraconContentBlockedException"/>), an
/// exhausted run budget, a missing attachment and a tool failure come from rings
/// inside this one; the request never reached the provider, or the provider
/// answered it. A permanent 4xx other than 401/403 rejects the request, not the
/// provider. The same rationale keeps <c>FinishReasonDetectingChatClient</c>
/// outside the circuit breaker.
/// </para>
/// </remarks>
internal sealed class CircuitBreakingChatClient(
    string providerName,
    IChatClient inner,
    ModelProviderCircuitBreaker breaker,
    string? credentialScope)
    : DelegatingChatClient(inner)
{
    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        breaker.EnsureRequestAllowed(providerName, credentialScope);

        try
        {
            var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            breaker.RecordSuccess(providerName, credentialScope);
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller cancellation says nothing about provider health.
            throw;
        }
        catch (Exception exception) when (CountsAgainstProvider(exception))
        {
            breaker.RecordFailure(providerName, credentialScope);
            throw;
        }
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        breaker.EnsureRequestAllowed(providerName, credentialScope);

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
                catch (Exception exception) when (CountsAgainstProvider(exception))
                {
                    breaker.RecordFailure(providerName, credentialScope);
                    throw;
                }

                if (!hasNext)
                {
                    break;
                }

                yield return enumerator.Current;
            }

            breaker.RecordSuccess(providerName, credentialScope);
        }
    }

    // This used to be a bare catch (Exception): every exception counted.
    // The setup-time credential's circuit is shared by every tenant, so one
    // tenant's repeated 400s opened it for all of them. An exhausted run budget,
    // a missing attachment and a tool failure the loop rethrows counted too:
    // those rings sit inside this one. Only the content block was excluded.

    /// <summary>
    /// Determines whether <paramref name="exception"/> shows that the provider,
    /// with this credential, is unhealthy.
    /// </summary>
    /// <param name="exception">The failure. The caller's own cancellation is filtered out before this is asked.</param>
    /// <returns>
    /// <see langword="true"/> for a cancellation the provider client raised itself
    /// (its own deadline) and for a failure the provider client threw, unless its
    /// HTTP status is a permanent 4xx other than 401 and 403.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The provider client's failures are recognized by the tag
    /// <c>ProviderFailureTaggingChatClient</c> puts on them at the raw client
    /// boundary (<see cref="ForeignProviderInvocationException"/>). A failure
    /// without the tag was thrown by a Tracon ring or a tool, above the provider.
    /// </para>
    /// <para>
    /// 401 and 403 count: the shared credential gets the same answer for every
    /// caller, so an invalid key still opens the circuit. 408, 429, 5xx
    /// and a failure without a status (connection refused, DNS) count. Every
    /// other 4xx rejects the request and gets the same answer on a healthy
    /// provider, so it does not count. The status is read with
    /// <see cref="ProviderHttpStatus"/>, the same reader the fallback chain and
    /// the workflow node retry use.
    /// </para>
    /// <para>
    /// A failure that does not count is neither a failure nor a success: it
    /// leaves the counter, and a half-open circuit, as they were.
    /// </para>
    /// </remarks>
    internal static bool CountsAgainstProvider(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // A cancellation nobody requested counted whatever raised it. A
        // tool's own timeout, rethrown by the function loop inside this ring,
        // opened the circuit every tenant shares. Only the provider client's own
        // deadline says something about the provider.
        if (exception is OperationCanceledException cancellation)
        {
            return ProviderFailureTaggingChatClient.IsProviderCancellation(cancellation);
        }

        if (ForeignProviderInvocationException.Find(exception) is not { } foreign)
        {
            return false;
        }

        return ProviderHttpStatus.Read(foreign.Original) is not { } status
            || status is 401 or 403
            || !ProviderHttpStatus.IsPermanent(status);
    }
}
