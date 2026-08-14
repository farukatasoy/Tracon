using System.Runtime.CompilerServices;
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

    /// <summary>
    /// <see cref="GetOrCreateSessionAsync"/> ile depoda kaydi bulunamayip TAZE
    /// acilan (henuz hicbir yere yazilmamis) oturumlari isaretler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// HATA-004: bu isaret, <see cref="SaveSessionAsync"/>'e "bu oturumun ILK
    /// kaydi mi" sorusunu depoya hic gitmeden yanitlatir. Oturum nesnesinin
    /// KENDISI anahtardir; <see cref="ConditionalWeakTable{TKey, TValue}"/>
    /// ek bir yasam donguson tesisati gerektirmeden (oturum GC'lendiginde
    /// kayit da duser) tek bir istek suresince guvenle tasinir.
    /// </para>
    /// <para>
    /// Isaret oturumun SERILESTIRILMIS durumuna KARISMAZ — yalniz bu surecin
    /// bellegindeki nesne kimligine bagli, geciçi bir isarettir.
    /// </para>
    /// </remarks>
    private readonly ConditionalWeakTable<AgentSession, object> _newlyOpenedSessions = new();

    private static readonly object NewSessionMarker = new();

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
            // 🚨 Depoya HENUZ HICBIR SEY YAZILMAZ. Konusma gecmisi
            // saglayicisinin (ChatHistoryProvider) konusma kimligi yalniz
            // agent GERCEKTEN calisirken uretilir — burada degil. Eszamanli
            // baska bir "ilk istek" de ayni sekilde bos bir oturumla baslar;
            // atomik iddia SaveSessionAsync'de, ilk kaydetme aninda yapilir
            // (bkz. o metodun aciklamasi, HATA-004).
            session = await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
            _newlyOpenedSessions.Add(session, NewSessionMarker);
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
    /// <exception cref="AgentPrismSessionConflictException">
    /// Bu, <see cref="GetOrCreateSessionAsync"/>'in TAZE actigi bir oturumun ILK
    /// kaydiydi ve ayni YENI oturum kimligini eszamanli baska bir istek bizden
    /// once kaydetti.
    /// </exception>
    /// <remarks>
    /// <para>
    /// HATA-004 (MT-CORE-054): ayni YENI oturum kimligine eszamanli iki ilk
    /// istek gelirse, ikisi de <see cref="GetOrCreateSessionAsync"/>'te BOS bir
    /// oturumla baslar ve KENDI turunu calistirir — her biri kendi konusma
    /// kimligini uretir, bu kacinilmazdir (konusma kimligi yalniz tur
    /// calisirken belli olur). Asil kusur, ikinci <see cref="ISessionStore.SaveAsync"/>'in
    /// birinciyi <strong>kosulsuzca</strong> ezip kaybedenin konusmasini
    /// sessizce erisilmez birakmasiydi. Bu yuzden bir oturumun ILK kaydi
    /// (bkz. <see cref="GetOrCreateSessionAsync"/>'in isaretledigi taze
    /// oturumlar) her zaman <see cref="ISessionStore.TryCreateAsync"/> ile
    /// atomik olarak dener; kaybederse SESSIZCE UZERINE YAZMAZ, acik bir
    /// <see cref="AgentPrismSessionConflictException"/> firlatir. Sonraki
    /// kayitlar (ve baştan bulunan mevcut oturumlarin HER kaydi) degismeden
    /// kosulsuz <see cref="ISessionStore.SaveAsync"/> kullanir.
    /// </para>
    /// </remarks>
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

        var record = new SessionRecord
        {
            Id = sessionId,
            AgentName = agent.Name ?? agent.Id,
            State = state,
            CreatedAt = now,
            UpdatedAt = now,
            TenantId = _tenantContext.TenantId,
        };

        if (_newlyOpenedSessions.TryGetValue(session, out _))
        {
            if (!await _store.TryCreateAsync(record, cancellationToken).ConfigureAwait(false))
            {
                throw new AgentPrismSessionConflictException(
                    $"'{sessionId}' oturumunu ayni anda baska bir istek de acti ve bizden once kaydetti. " +
                    "Kisa bir sure sonra yeniden deneyin.")
                {
                    SessionId = sessionId,
                };
            }

            _newlyOpenedSessions.Remove(session);
        }
        else
        {
            await _store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        }

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
