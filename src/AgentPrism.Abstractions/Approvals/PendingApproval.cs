namespace AgentPrism;

/// <summary>
/// Kuyruktan kosan bir calistirmanin bekleyen tool onay istegi.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 Bu kayit bir <strong>izdusumdur</strong>, sahibi degildir: tek gercek
/// kaynak MAF'in oturum durumudur (<c>ToolApprovalRequestContent</c>, oturum
/// gecmisinde yasar). Karar uygulanirken oturum okunur, bu tablo degil.
/// </para>
/// <para>
/// Bekleyen istegin ait oldugu calistirma <see cref="RunStatus.AwaitingApproval"/>
/// ile kapanir ve bir daha degismez (K-014). Karar verildiginde AYNI
/// calistirma surmez; <strong>yeni</strong> bir calistirma kuyruga dusurulur
/// (ayni <see cref="SessionId"/>, yeni bir <c>RunId</c>).
/// </para>
/// </remarks>
public sealed record PendingApproval
{
    /// <summary>Kayit kimligi.</summary>
    public required Guid Id { get; init; }

    /// <summary>Kiraci kimligi.</summary>
    public required string TenantId { get; init; }

    /// <summary>Bu istegi ureten, <see cref="RunStatus.AwaitingApproval"/> ile kapanmis calistirma.</summary>
    public required Guid RunId { get; init; }

    /// <summary>
    /// Calistirmanin oturumu. Karar uygulanirken devam eden calistirma bu
    /// oturumla kuyruga dusurulur.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>MAF'in urettigi <c>ToolApprovalRequestContent.RequestId</c> degeri.</summary>
    public required string RequestId { get; init; }

    /// <summary>Onay isteyen tool'un adi.</summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// Tool cagrisinin argumanlari, <c>anahtar=deger</c> cifleri olarak
    /// (AOT uyumlu kalmak icin yansimaya dayanan JSON serilestirme
    /// KULLANILMAZ). Kayit ayarlarindaki <c>RecordToolPayloads</c> kapaliysa
    /// <see langword="null"/> kalir.
    /// </summary>
    public string? Arguments { get; init; }

    /// <summary>Istegin durumu.</summary>
    public required ApprovalStatus Status { get; init; }

    /// <summary>Karari veren aktor. Karar verilmediyse <see langword="null"/>.</summary>
    public string? DecidedBy { get; init; }

    /// <summary>Karar ani. Karar verilmediyse <see langword="null"/>.</summary>
    public DateTimeOffset? DecidedAt { get; init; }

    /// <summary>
    /// Bu andan sonra istek <see cref="ApprovalStatus.Expired"/> sayilir.
    /// </summary>
    /// <remarks>Zorunludur: suresiz bekleyen bir onay istegi bir sizintidir.</remarks>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Olusturulma ani.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}
