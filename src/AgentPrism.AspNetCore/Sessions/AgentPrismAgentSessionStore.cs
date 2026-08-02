using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;

namespace AgentPrism;

/// <summary>
/// Microsoft Agent Framework'un <see cref="AgentSessionStore"/> soyutlamasini
/// AgentPrism'in <see cref="AgentSessionManager"/> sinifina baglar.
/// </summary>
/// <remarks>
/// <para>
/// Kendi kalicilik kodunu <strong>yazmaz</strong>; yalnizca delege eder. Boylece
/// oturumlar hangi yoldan gelirse gelsin (yonetim API'si, OpenAI uyumlu uclar veya
/// MAF barindirma yapilari) ayni depoya, ayni kimlik damgasiyla ve ayni kiraci
/// yalitimi ile yazilir.
/// </para>
/// <para>
/// Bu sinif <c>AgentPrism.AspNetCore</c> icindedir cunku <see cref="AgentSessionStore"/>
/// on surum <c>Microsoft.Agents.AI.Hosting</c> paketindedir ve K-008 geregi
/// <c>AgentPrism.Core</c> o pakete baglanamaz.
/// Gerekce: <c>docs/KARARLAR.md</c>, kararlar K-008 ve K-026.
/// </para>
/// <para>
/// Cok kiracili kurulumlarda bu ornek MAF'in
/// <c>IsolationKeyScopedAgentSessionStore</c> sinifi ile sarmalanabilir; bunun icin
/// tuketici kendi <see cref="AgentSessionStore"/> kaydini <c>MapAgentPrism</c>
/// cagrisindan once yapar.
/// </para>
/// </remarks>
public sealed class AgentPrismAgentSessionStore : AgentSessionStore
{
    private readonly AgentSessionManager _sessions;

    /// <summary>Yeni bir kopru olusturur.</summary>
    /// <param name="sessions">Delege edilecek oturum yoneticisi.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sessions"/> <see langword="null"/> ise.</exception>
    public AgentPrismAgentSessionStore(AgentSessionManager sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        _sessions = sessions;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Kayit yoksa yeni bir oturum acilir. Donen oturum her iki durumda da
    /// <paramref name="sessionStoreId"/> ile damgalanmistir.
    /// </remarks>
    public override ValueTask<AgentSession> GetSessionAsync(
        AIAgent agent,
        string sessionStoreId,
        CancellationToken cancellationToken = default)
        => _sessions.GetOrCreateSessionAsync(agent, sessionStoreId, cancellationToken);

    /// <inheritdoc />
    public override async ValueTask SaveSessionAsync(
        AIAgent agent,
        string sessionStoreId,
        AgentSession session,
        CancellationToken cancellationToken = default)
    {
        // Oturum bu kopru disinda acilmis olabilir (MAF barindirma yapilari kendi
        // oturumlarini uretir). Kaydetmeden once kimligi damgalamak, kaydin dogru
        // anahtarla yazilmasini ve calistirma kayitlarinin oturumu bulabilmesini saglar.
        AgentSessionIdentity.SetId(session, sessionStoreId);

        await _sessions.SaveSessionAsync(agent, session, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async ValueTask DeleteSessionAsync(
        AIAgent agent,
        string sessionStoreId,
        CancellationToken cancellationToken = default)
        => await _sessions.DeleteSessionAsync(sessionStoreId, cancellationToken).ConfigureAwait(false);
}
