namespace AgentPrism;

/// <summary>
/// Yuklenmis bir ekin ustverisi.
/// </summary>
/// <remarks>
/// Ikili icerik burada TASINMAZ. <see cref="IAttachmentStore.OpenReadAsync"/> ile
/// ayrica okunur. Gerekce: <c>docs/14-COK-MODLULUK.md</c>, bolum 14.1 — mesaj
/// govdesi kucuk kalmalidir.
/// </remarks>
public sealed record AttachmentDescriptor
{
    /// <summary>Ek kimligi.</summary>
    public required Guid Id { get; init; }

    /// <summary>Ekin ait oldugu kiraci.</summary>
    public required string TenantId { get; init; }

    /// <summary>Ekin yuklendigi oturum. Oturumsuz yuklemede <see langword="null"/>.</summary>
    public string? SessionId { get; init; }

    /// <summary>Ekin uretildigi calistirma. Kullanici yuklemesinde <see langword="null"/>.</summary>
    public Guid? RunId { get; init; }

    /// <summary>Orijinal dosya adi.</summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Dogrulanmis MIME turu. Istemcinin gonderdigi <c>Content-Type</c> degil,
    /// sihirli bayt denetiminin sonucudur.
    /// </summary>
    public required string MediaType { get; init; }

    /// <summary>Icerigin bayt cinsinden boyutu.</summary>
    public required long ByteSize { get; init; }

    /// <summary>Icerigin SHA-256 ozeti (onaltilik, buyuk harf).</summary>
    public required string Sha256 { get; init; }

    /// <summary>Yukleyen aktor. Bilinmiyorsa <see langword="null"/>.</summary>
    public string? CreatedBy { get; init; }

    /// <summary>Yukleme zamani.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}
