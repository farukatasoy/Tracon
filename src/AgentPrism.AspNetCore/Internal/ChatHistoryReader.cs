using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Saklanmis bir oturumun sohbet gecmisini okur.
/// </summary>
/// <remarks>
/// <para>
/// Gecmis, kayitli <see cref="ChatHistoryProvider"/> uzerinden okunur.
/// <c>InvokingAsync</c> ve <c>InvokingContext</c> kurucusu Microsoft Agent
/// Framework'un public yuzeyindedir; saglayici bu cagrida yalnizca okur, hicbir sey
/// yazmaz. Ayni yol hem bellek ici hem PostgreSQL kurulumunda calisir
/// (<c>AddAgentPrism()</c> saglayiciyi acikca kaydeder, karar K-037).
/// </para>
/// <para>
/// Hem yonetim API'si (<c>/api/sessions/{id}</c>) hem OpenAI uyumlu Conversations
/// ucu (<c>/v1/conversations/{id}/items</c>) buradan okur; iki uc ayni gercegi
/// gostermelidir.
/// </para>
/// </remarks>
internal static class ChatHistoryReader
{
    /// <summary>Bir oturum kaydinin sohbet gecmisini okur.</summary>
    /// <param name="record">Oturum kaydi.</param>
    /// <param name="catalog">Agent katalogu.</param>
    /// <param name="chatHistory">Kayitli sohbet gecmisi saglayicisi.</param>
    /// <param name="loggerFactory">Gunlukleyici fabrikasi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>
    /// Mesajlar; gecmis okunamadiysa <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// Gecmisin okunamamasi bir hata <strong>degildir</strong>: agent silinmis,
    /// saglayicisi kaldirilmis veya MAF serilestirme bicimi degismis olabilir.
    /// Gozlemlenebilirlik islevselligi bozmaz; cagiran taraf ustveriyi yine dondurur.
    /// </remarks>
    public static async ValueTask<IReadOnlyList<ChatMessage>?> ReadAsync(
        SessionRecord record,
        IAgentCatalog catalog,
        ChatHistoryProvider chatHistory,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            var agent = await catalog.ResolveAsync(record.AgentName, cancellationToken).ConfigureAwait(false);

            if (agent is null)
            {
                return null;
            }

            var session = await agent
                .DeserializeSessionAsync(record.State, jsonSerializerOptions: null, cancellationToken)
                .ConfigureAwait(false);

            // MAAI001: InvokingContext kurucusu "for evaluation purposes only" isaretlidir
            // ve TreatWarningsAsErrors ile build'i kirar. Bastirma bilincli ve TEK
            // NOKTADADIR: gecmisi okumanin baska public yolu yoktur
            // (ProvideChatHistoryAsync protected'tir) ve bu cagri yalnizca okur.
            // MAF bu API'yi degistirirse yalnizca burasi guncellenir.
            // Gerekce: docs/KARARLAR.md, karar K-037.
#pragma warning disable MAAI001
            var context = new ChatHistoryProvider.InvokingContext(agent, session, []);
#pragma warning restore MAAI001

            var messages = await chatHistory.InvokingAsync(context, cancellationToken).ConfigureAwait(false);

            return [.. messages];
        }
        catch (Exception ex) when (ex is AgentPrismException or System.Text.Json.JsonException or InvalidOperationException or NotSupportedException)
        {
            loggerFactory
                .CreateLogger(typeof(ChatHistoryReader).FullName!)
                .LogWarning(
                    ex,
                    "'{SessionId}' oturumunun sohbet gecmisi okunamadi. Ustveri gecmissiz donduruluyor.",
                    record.Id);

            return null;
        }
    }
}
