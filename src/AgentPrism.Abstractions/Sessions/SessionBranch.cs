namespace AgentPrism;

/// <summary>Bir konusmayi belirli bir noktadan dallandirma istegi.</summary>
public sealed record SessionBranchRequest
{
    /// <summary>
    /// Dahil edilecek son ogenin sira numarasi (dahil). Verilmezse konusmanin
    /// tamami kopyalanir.
    /// </summary>
    /// <remarks>
    /// <c>0</c> gecerli bir degerdir ve yalnizca ilk ogeyi tasiyan bir dal acar.
    /// Negatif bir deger reddedilir.
    /// </remarks>
    public long? UpToSequence { get; init; }

    /// <summary>
    /// Acilacak yeni oturumun kimligi. Verilmezse uretilir.
    /// </summary>
    /// <remarks>
    /// Kimlik cagirandan gelirse ayni kimlikle var olan bir oturum <strong>uzerine
    /// yazilmaz</strong>; istek reddedilir.
    /// </remarks>
    public string? NewSessionId { get; init; }
}

/// <summary>Dallandirma sonucu.</summary>
public sealed record SessionBranchResult
{
    /// <summary>Yeni oturumun kimligi.</summary>
    public required string SessionId { get; init; }

    /// <summary>Yeni konusmanin kimligi.</summary>
    public required Guid ConversationId { get; init; }

    /// <summary>Kaynak oturumun kimligi.</summary>
    public required string ParentSessionId { get; init; }

    /// <summary>Kaynak konusmanin kimligi.</summary>
    public required Guid ParentConversationId { get; init; }

    /// <summary>
    /// Dal noktasi: yeni konusmaya kopyalanan son ogenin sira numarasi.
    /// Hicbir oge kopyalanmadiysa <c>-1</c>.
    /// </summary>
    public required long BranchFromSequence { get; init; }

    /// <summary>Kopyalanan oge sayisi.</summary>
    public required int CopiedItemCount { get; init; }
}

/// <summary>
/// Bir konusmayi kopyalayarak dallandiran depo.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 <strong>Kopyalama secildi, isaretci zinciri degil.</strong> Yeni konusma
/// yalnizca <c>parent_conversation_id</c> tutsaydi her gecmis okumasi
/// ozyinelemeli olurdu; <c>SqlChatHistoryProvider</c> her agent turunda calisan
/// en sicak okuma yoludur ve dallanma kullanmayan tuketiciye de bedel odetirdi.
/// Kopyalamayla okuma yolu <strong>hic degismez</strong>. Isaretci yalnizca
/// koken bilgisidir.
/// </para>
/// <para>
/// 🚨 Bu arayuz yalnizca bir SQL saglayicisi kayitliyken (<c>UsePostgreSql()</c>,
/// <c>UseSqlServer()</c>, <c>UseSqlite()</c>) kaydedilir. Bellek ici kurulumda
/// sohbet gecmisi Microsoft Agent Framework'un <c>InMemoryChatHistoryProvider</c>
/// nesnesinde, oturum durumunun <strong>opak</strong> blogunda yasar ve belirli
/// bir sira numarasina kadar kopyalanamaz. Uc bu durumda <c>501</c> doner —
/// sessizce tamamini kopyalamaz.
/// </para>
/// </remarks>
public interface IConversationBranchStore
{
    /// <summary>Bir konusmayi kopyalayarak yeni bir konusma acar.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="parentConversationId">Kaynak konusma.</param>
    /// <param name="upToSequence">
    /// Dahil edilecek son ogenin sira numarasi. <see langword="null"/> ise
    /// konusmanin tamami kopyalanir.
    /// </param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>
    /// Yeni konusmanin kimligi, dal noktasi ve kopyalanan oge sayisi; kaynak
    /// konusma yoksa veya baska bir kiraciya aitse <see langword="null"/>.
    /// </returns>
    ValueTask<ConversationBranch?> BranchAsync(
        string tenantId,
        Guid parentConversationId,
        long? upToSequence,
        CancellationToken cancellationToken = default);
}

/// <summary>Bir konusma dalinin depo duzeyindeki sonucu.</summary>
/// <param name="ConversationId">Yeni konusmanin kimligi.</param>
/// <param name="BranchFromSequence">
/// Kopyalanan son ogenin sira numarasi; hicbir oge kopyalanmadiysa <c>-1</c>.
/// </param>
/// <param name="CopiedItemCount">Kopyalanan oge sayisi.</param>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct ConversationBranch(
    Guid ConversationId,
    long BranchFromSequence,
    int CopiedItemCount);
