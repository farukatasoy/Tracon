using System.Text.Json;
using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// Adlandirilmis oturumlarin yasam dongusunu yonetir: <see cref="ISessionStore"/>
/// icinden yukler, geri yukler ve kaydeder.
/// </summary>
/// <remarks>
/// <para>
/// Bu sinif saglayicidan bagimsizdir. Bellek ici depoyla da, PostgreSQL deposuyla
/// da ayni sekilde calisir; hangi deponun kayitli oldugunu bilmez.
/// </para>
/// <para>
/// Oturum durumu Microsoft Agent Framework'un <c>SerializeSessionAsync</c> ciktisidir
/// ve <strong>opak</strong> kabul edilir. AgentPrism icerigini yorumlamaz.
/// Kimlik, <see cref="AgentSessionIdentity"/> ile oturumun durum cantasina damgalanir;
/// boylece <c>RunRecordingAgent</c> calistirma kaydina gercek oturum kimligini yazabilir.
/// </para>
/// <para>
/// Microsoft Agent Framework'un on surum <c>AgentSessionStore</c> soyutlamasi
/// (<c>Microsoft.Agents.AI.Hosting</c>) bu sinifa delege edilerek Faz 4'te baglanir.
/// Gerekce: <c>docs/KARARLAR.md</c>, karar K-008 ve K-026.
/// </para>
/// </remarks>
public sealed class AgentSessionManager
{
    private readonly ISessionStore _store;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    /// <summary>Yeni bir oturum yoneticisi olusturur.</summary>
    /// <param name="store">Oturum deposu.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <param name="timeProvider">Zaman kaynagi. Verilmezse sistem saati kullanilir.</param>
    /// <exception cref="ArgumentNullException">Zorunlu bagimliliklardan biri <see langword="null"/> ise.</exception>
    public AgentSessionManager(ISessionStore store, ITenantContext tenantContext, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _store = store;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Kimligi verilen oturumu geri yukler; kayit yoksa yeni bir oturum acar.
    /// Her iki durumda da donen oturum kendi kimligiyle damgalanmistir.
    /// </summary>
    /// <param name="agent">Oturumun ait oldugu agent.</param>
    /// <param name="sessionId">Oturum kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kullanima hazir oturum.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="agent"/> <see langword="null"/> ise.</exception>
    /// <exception cref="ArgumentException"><paramref name="sessionId"/> bos ise.</exception>
    /// <exception cref="AgentPrismException">Saklanan durum bu agent tarafindan geri yuklenemezse.</exception>
    public async ValueTask<AgentSession> GetOrCreateSessionAsync(
        AIAgent agent,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var record = await _store.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);

        AgentSession session;

        if (record is null)
        {
            session = await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            try
            {
                session = await agent
                    .DeserializeSessionAsync(record.State, jsonSerializerOptions: null, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException or ArgumentException)
            {
                throw new AgentPrismException(
                    $"'{sessionId}' oturumu geri yuklenemedi. Saklanan durum '{record.AgentName}' agent'ina aittir " +
                    $"ve '{agent.Name ?? agent.Id}' agent'i tarafindan okunamiyor. " +
                    "Microsoft Agent Framework surumu degistiyse eski oturumlar okunamaz hale gelmis olabilir.",
                    ex);
            }
        }

        AgentSessionIdentity.SetId(session, sessionId);
        return session;
    }

    /// <summary>Oturumu serilestirir ve depoya yazar.</summary>
    /// <param name="agent">Oturumun ait oldugu agent.</param>
    /// <param name="session">Kaydedilecek oturum.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kaydedilen oturumun kimligi.</returns>
    /// <exception cref="ArgumentNullException">Parametrelerden biri <see langword="null"/> ise.</exception>
    /// <exception cref="AgentPrismException">
    /// Oturum <see cref="GetOrCreateSessionAsync"/> ile acilmadigi icin kimligi yoksa.
    /// </exception>
    public async ValueTask<string> SaveSessionAsync(
        AIAgent agent,
        AgentSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(session);

        var sessionId = AgentSessionIdentity.GetId(session)
            ?? throw new AgentPrismException(
                "Oturumun AgentPrism kimligi yok. Kaydedilecek oturumlar " +
                $"{nameof(GetOrCreateSessionAsync)} ile acilmalidir.");

        var state = await agent
            .SerializeSessionAsync(session, jsonSerializerOptions: null, cancellationToken)
            .ConfigureAwait(false);

        var now = _timeProvider.GetUtcNow();

        await _store.SaveAsync(
            new SessionRecord
            {
                Id = sessionId,
                AgentName = agent.Name ?? agent.Id,
                State = state,
                CreatedAt = now,
                UpdatedAt = now,
                TenantId = _tenantContext.TenantId,
            },
            cancellationToken).ConfigureAwait(false);

        return sessionId;
    }

    /// <summary>Oturumu siler.</summary>
    /// <param name="sessionId">Oturum kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Silme gerceklestiyse <see langword="true"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="sessionId"/> bos ise.</exception>
    public ValueTask<bool> DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return _store.DeleteAsync(sessionId, cancellationToken);
    }

    /// <summary>Oturumlari filtreleyerek listeler.</summary>
    /// <param name="query">Filtre. Kiraci belirtilmemisse gecerli kiraci kullanilir.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Oturumlar.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> <see langword="null"/> ise.</exception>
    public ValueTask<IReadOnlyList<SessionRecord>> QuerySessionsAsync(
        SessionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return _store.QueryAsync(
            query.TenantId is null ? query with { TenantId = _tenantContext.TenantId } : query,
            cancellationToken);
    }
}
