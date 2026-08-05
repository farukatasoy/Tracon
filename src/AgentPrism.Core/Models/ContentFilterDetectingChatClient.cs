using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Icerik/guvenlik filtresiyle kesilmis <strong>bos</strong> bir yaniti
/// <see cref="AgentPrismContentFilteredException"/> ile hataya cevirir.
/// </summary>
/// <remarks>
/// <para>
/// Saglayici uygulamasinin icine gomulmez; <see cref="ModelProviderRegistry.CreateChatClient"/>
/// her istemciyi bu tipe sarar — devre kesici ile ayni desen (Faz 8). Boylece OpenAI,
/// Anthropic ve Gemini ayni davranisi tek bir yerden alir ve Faz 27 (Azure) kural
/// yazmadan devralir.
/// </para>
/// <para>
/// <strong>Sarmalama sirasi onemlidir:</strong> bu dekorator devre kesicinin
/// <em>disinda</em> durur. Filtrelenmis bir yanit saglayicinin saglikli oldugu
/// anlamina gelir; devre kesicinin icinde olsaydi attigi istisna ardisik hata
/// sayacini artirir ve icerigi filtrelenen birkac istek saglayiciyi kapatirdi.
/// </para>
/// <para>
/// <strong>Yalnizca bos yanit hataya cevrilir.</strong> Model metin uretip sonra
/// kesildiyse (Gemini'nin sik davranisi degil, ama mumkun) kullanicinin elinde
/// kismi bir cevap vardir; onu silmek bilgi kaybidir. Bos yanit ise sessiz
/// birakildiginda hata ayiklamasi en zor durumu uretir: kullanici bos bir cevap
/// gorur ve kayitta hicbir iz yoktur.
/// </para>
/// </remarks>
internal sealed class ContentFilterDetectingChatClient(string providerName, IChatClient inner)
    : DelegatingChatClient(inner)
{
    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

        if (IsContentFilter(response.FinishReason) && !HasContent(response))
        {
            throw Filtered(response.FinishReason);
        }

        return response;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ChatFinishReason? finishReason = null;
        var sawContent = false;

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken)
            .ConfigureAwait(false))
        {
            finishReason ??= update.FinishReason;
            sawContent = sawContent || HasContent(update.Contents);

            yield return update;
        }

        // Karar akisin SONUNDA verilir: bitis sebebi cogu saglayicida son cercevede
        // gelir ve ondan once metin uretilmis olabilir.
        if (IsContentFilter(finishReason) && !sawContent)
        {
            throw Filtered(finishReason);
        }
    }

    private static bool IsContentFilter(ChatFinishReason? finishReason)
        => finishReason == ChatFinishReason.ContentFilter;

    private static bool HasContent(ChatResponse response)
    {
        if (!string.IsNullOrWhiteSpace(response.Text))
        {
            return true;
        }

        foreach (var message in response.Messages)
        {
            if (HasContent(message.Contents))
            {
                return true;
            }
        }

        return false;
    }

    // Metin disindaki icerikler de "bos degil" sayilir: bir tool cagrisi veya
    // uretilmis bir goruntu, yanitin kullanilabilir oldugunu gosterir. Yalniz
    // kullanim sayaclari (UsageContent) icerik degildir; filtrelenen bir yanit
    // da token harcar.
    private static bool HasContent(IEnumerable<AIContent> contents)
    {
        foreach (var content in contents)
        {
            switch (content)
            {
                case TextContent text when !string.IsNullOrWhiteSpace(text.Text):
                case FunctionCallContent:
                case DataContent:
                case UriContent:
                    return true;

                default:
                    break;
            }
        }

        return false;
    }

    private AgentPrismContentFilteredException Filtered(ChatFinishReason? finishReason)
        => new($"'{providerName}' saglayicisi yaniti icerik filtresiyle kesti ve hicbir icerik dondurmedi. " +
               "Bu bir basarisiz calistirmadir; istek yeniden denenmeden once girdi gozden gecirilmelidir. " +
               "Gemini'de guvenlik esikleri ModelBinding.ProviderSettings ile gevsetilebilir " +
               "(ornek: google.safety.harassment = \"BLOCK_ONLY_HIGH\").")
        {
            ProviderName = providerName,
            FinishReason = finishReason?.Value,
        };
}
