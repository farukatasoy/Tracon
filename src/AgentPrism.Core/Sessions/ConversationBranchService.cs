using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// Bir oturumun konusmasini belirli bir noktadan dallandirir ve dali tasiyan
/// yeni bir oturum acar.
/// </summary>
/// <remarks>
/// <para>
/// Dallandirma <strong>kopyalar</strong>: ogeler yeni konusmaya
/// <c>INSERT … SELECT</c> ile tasinir, isaretci yalnizca koken bilgisidir.
/// Isaretci zinciri secilseydi her gecmis okumasi ozyinelemeli olurdu ve
/// <c>SqlChatHistoryProvider</c> — her agent turunda calisan en sicak okuma
/// yolu — dallanma kullanmayan tuketiciye de bedel odetirdi. Bu tasarimda okuma
/// yolu <strong>tek satir bile degismez</strong>.
/// </para>
/// <para>
/// Konusma kimligi oturumun durum cantasinda yasar
/// (<see cref="AgentPrismSessionStateKeys.ChatHistory"/>); dallanan konusmayi
/// kullanmanin tek yolu o kimligi tasiyan yeni bir oturumdur. Uc bu yuzden
/// oturum uzerindedir, konusma uzerinde degil.
/// </para>
/// </remarks>
public sealed class ConversationBranchService
{
    private readonly ISessionStore _sessions;
    private readonly IAgentCatalog _catalog;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;
    private readonly IConversationBranchStore? _branchStore;

    /// <summary>Yeni bir dallandirma servisi olusturur.</summary>
    /// <param name="sessions">Oturum deposu.</param>
    /// <param name="catalog">Agent katalogu. Oturumu geri yuklemek icin gerekir.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <param name="branchStore">
    /// Konusma kopyalayici. <see langword="null"/> ise dallandirma desteklenmez —
    /// bellek ici kurulumda sohbet gecmisi Microsoft Agent Framework'un
    /// <c>InMemoryChatHistoryProvider</c> nesnesinde, oturum durumunun opak
    /// blogunda yasar ve belirli bir sira numarasina kadar kopyalanamaz.
    /// </param>
    /// <param name="timeProvider">Zaman kaynagi. Verilmezse sistem saati kullanilir.</param>
    /// <exception cref="ArgumentNullException">Zorunlu bagimliliklardan biri <see langword="null"/> ise.</exception>
    public ConversationBranchService(
        ISessionStore sessions,
        IAgentCatalog catalog,
        ITenantContext tenantContext,
        IConversationBranchStore? branchStore = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _sessions = sessions;
        _catalog = catalog;
        _tenantContext = tenantContext;
        _branchStore = branchStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Bu kurulumda dallandirma destekleniyor mu.
    /// </summary>
    /// <remarks>
    /// Yalnizca kalici bir SQL saglayicisi (<c>UsePostgreSql()</c>,
    /// <c>UseSqlServer()</c>, <c>UseSqlite()</c>) acikken <see langword="true"/>.
    /// </remarks>
    public bool IsSupported => _branchStore is not null;

    /// <summary>Bir oturumun konusmasini dallandirir.</summary>
    /// <param name="sessionId">Kaynak oturum.</param>
    /// <param name="request">Dallandirma istegi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Dallandirma sonucu.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> <see langword="null"/> ise.</exception>
    /// <exception cref="ArgumentException"><paramref name="sessionId"/> bos ise.</exception>
    public async ValueTask<SessionBranchOutcome> BranchAsync(
        string sessionId,
        SessionBranchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(request);

        if (_branchStore is null)
        {
            return SessionBranchOutcome.Failed(
                SessionBranchStatus.NotSupported,
                "Konusma dallandirma yalnizca kalici bir SQL saglayicisi acikken calisir. " +
                "Bellek ici kurulumda sohbet gecmisi oturum durumunun opak blogunda yasar ve " +
                "belirli bir noktaya kadar kopyalanamaz; sessizce tamamini kopyalamak " +
                "istenen dali uretmezdi.");
        }

        if (request.UpToSequence is < 0)
        {
            return SessionBranchOutcome.Failed(
                SessionBranchStatus.InvalidRequest,
                "'upToSequence' negatif olamaz. Bos birakilirsa konusmanin tamami kopyalanir.");
        }

        var record = await _sessions.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);

        // "Yok" ile "baska kiraciya ait" AYNI sonucu verir; varlik sizdirmaz.
        if (record is null || !string.Equals(record.TenantId, _tenantContext.TenantId, StringComparison.Ordinal))
        {
            return SessionBranchOutcome.Failed(
                SessionBranchStatus.SessionNotFound,
                $"'{sessionId}' kimlikli bir oturum yok.");
        }

        var newSessionId = request.NewSessionId is { Length: > 0 } requested
            ? requested
            : AgentPrismId.NewId().ToString("D");

        if (await _sessions.GetAsync(newSessionId, cancellationToken).ConfigureAwait(false) is not null)
        {
            return SessionBranchOutcome.Failed(
                SessionBranchStatus.SessionExists,
                $"'{newSessionId}' kimlikli bir oturum zaten var. Dal, var olan bir oturumun " +
                "uzerine YAZMAZ; baska bir kimlik verin veya bos birakip uretilmesini saglayin.");
        }

        var agent = await _catalog.ResolveAsync(record.AgentName, cancellationToken).ConfigureAwait(false);

        if (agent is null)
        {
            return SessionBranchOutcome.Failed(
                SessionBranchStatus.AgentNotFound,
                $"'{record.AgentName}' adinda bir agent yok; oturum geri yuklenemedigi icin " +
                "konusmasi dallandirilamaz.");
        }

        AgentSession session;

        try
        {
            session = await agent
                .DeserializeSessionAsync(record.State, jsonSerializerOptions: null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            return SessionBranchOutcome.Failed(
                SessionBranchStatus.AgentNotFound,
                $"'{sessionId}' oturumu '{record.AgentName}' agent'i ile geri yuklenemedi: {ex.Message}");
        }

        if (!session.StateBag.TryGetValue<ChatHistoryState>(
                AgentPrismSessionStateKeys.ChatHistory,
                out var state,
                AgentPrismCoreJsonContext.Default.Options) ||
            state is null ||
            state.ConversationId == Guid.Empty)
        {
            return SessionBranchOutcome.Failed(
                SessionBranchStatus.NoConversation,
                $"'{sessionId}' oturumunun henuz bir konusmasi yok. Dallandirilacak bir sey " +
                "olmasi icin oturumda en az bir tur calismis olmalidir.");
        }

        var branch = await _branchStore
            .BranchAsync(_tenantContext.TenantId, state.ConversationId, request.UpToSequence, cancellationToken)
            .ConfigureAwait(false);

        if (branch is not { } created)
        {
            return SessionBranchOutcome.Failed(
                SessionBranchStatus.NoConversation,
                $"'{state.ConversationId}' konusmasi bulunamadi; oturum bir konusmaya isaret ediyor " +
                "ama kayit silinmis olabilir.");
        }

        // Yeni oturum, kaynagin durumunu aynen tasir; yalniz konusma kimligi ve
        // AgentPrism kimlik damgasi degisir. Bu, dallanan konusmayi okuma yolunu
        // hic degistirmeden kullanmanin tek yoludur.
        session.StateBag.SetValue(
            AgentPrismSessionStateKeys.ChatHistory,
            new ChatHistoryState { ConversationId = created.ConversationId },
            AgentPrismCoreJsonContext.Default.Options);

        AgentSessionIdentity.SetId(session, newSessionId);

        var now = _timeProvider.GetUtcNow();

        await _sessions.SaveAsync(
            new SessionRecord
            {
                Id = newSessionId,
                AgentName = record.AgentName,
                State = await agent
                    .SerializeSessionAsync(session, jsonSerializerOptions: null, cancellationToken)
                    .ConfigureAwait(false),
                CreatedAt = now,
                UpdatedAt = now,
                TenantId = _tenantContext.TenantId,
            },
            cancellationToken).ConfigureAwait(false);

        return SessionBranchOutcome.Succeeded(new SessionBranchResult
        {
            SessionId = newSessionId,
            ConversationId = created.ConversationId,
            ParentSessionId = sessionId,
            ParentConversationId = state.ConversationId,
            BranchFromSequence = created.BranchFromSequence,
            CopiedItemCount = created.CopiedItemCount,
        });
    }
}

/// <summary>Dallandirma denemesinin sonucu.</summary>
public enum SessionBranchStatus
{
    /// <summary>Dal acildi.</summary>
    Branched = 0,

    /// <summary>Kaynak oturum yok veya baska bir kiraciya ait.</summary>
    SessionNotFound = 1,

    /// <summary>Oturumun henuz bir konusmasi yok.</summary>
    NoConversation = 2,

    /// <summary>Istenen yeni oturum kimligi zaten kullanimda.</summary>
    SessionExists = 3,

    /// <summary>Oturumun agent'i cozulemiyor.</summary>
    AgentNotFound = 4,

    /// <summary>Istek gecersiz.</summary>
    InvalidRequest = 5,

    /// <summary>Bu kurulumda dallandirma desteklenmiyor.</summary>
    NotSupported = 6,
}

/// <summary>Bir dallandirma denemesinin sonucu.</summary>
public sealed record SessionBranchOutcome
{
    private SessionBranchOutcome()
    {
    }

    /// <summary>Sonuc.</summary>
    public required SessionBranchStatus Status { get; init; }

    /// <summary>Basarisizligin insan okunur gerekcesi.</summary>
    public string? Detail { get; init; }

    /// <summary>Acilan dal. Yalnizca <see cref="SessionBranchStatus.Branched"/> iken dolu.</summary>
    public SessionBranchResult? Result { get; init; }

    /// <summary>Basarisiz bir sonuc uretir.</summary>
    /// <param name="status">Sonuc.</param>
    /// <param name="detail">Gerekce.</param>
    /// <returns>Sonuc.</returns>
    public static SessionBranchOutcome Failed(SessionBranchStatus status, string detail)
        => new() { Status = status, Detail = detail };

    /// <summary>Basarili bir sonuc uretir.</summary>
    /// <param name="result">Acilan dal.</param>
    /// <returns>Sonuc.</returns>
    public static SessionBranchOutcome Succeeded(SessionBranchResult result)
        => new() { Status = SessionBranchStatus.Branched, Result = result };
}
