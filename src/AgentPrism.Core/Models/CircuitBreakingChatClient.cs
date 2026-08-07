using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Bir <see cref="IChatClient"/>'i <see cref="ModelProviderCircuitBreaker"/> ile sarar.
/// </summary>
/// <remarks>
/// <para>
/// Saglayici uygulamasinin (ornegin <c>AgentPrism.OpenAI</c>) icine gomulmez;
/// <see cref="ModelProviderRegistry.CreateChatClient"/> her istemciyi bu tipe sarar.
/// Boylece her saglayici (bugunku OpenAI, gelecekteki Anthropic/Gemini) ayni korumayi
/// bedava alir. Gerekce: <c>docs/08-SAGLAYICI-GENISLEMESI.md</c>, bolum 8.3.
/// </para>
/// <para>
/// 🚨 <strong>Icerik engellemesi hata SAYILMAZ</strong> (Faz 48). Bir
/// <see cref="AgentPrismContentBlockedException"/> saglayicinin saglikli oldugunu
/// gosterir: istek bize takildi, aga hic cikmadi. Sayilsaydi arka arkaya
/// engellenen birkac istek saglayiciyi kapatirdi ve bir politika karari bir
/// kesintiye donusurdu. Ayni gerekce <c>ContentFilterDetectingChatClient</c>'i
/// devre kesicinin disinda tutar; guard ise dongunun ICINDE oldugu icin ayiklama
/// burada yapilir.
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
            // Cagiranin iptali saglayici sagligi hakkinda bir sey soylemez.
            throw;
        }
        catch (AgentPrismContentBlockedException)
        {
            // Icerik guard'inin karari da soylemez: istek aga hic cikmadi.
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
