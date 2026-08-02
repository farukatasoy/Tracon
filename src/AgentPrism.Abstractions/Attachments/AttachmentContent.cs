namespace AgentPrism;

/// <summary>
/// Depoya kaydedilecek yeni bir ekin icerigi.
/// </summary>
/// <remarks>
/// Boyut sinirinin (varsayilan 20 MB) altinda kaldigi icin icerik onceden
/// bellege okunmus olarak tasinir; depo katmani akis yonetimiyle ugrasmaz.
/// Turun kendisi bir <c>record</c> degildir: <see cref="Data"/> alani buyuk
/// olabilecegi icin uretilen <c>ToString</c>/esitlik karsilastirmasinin
/// yanlislikla tum icerigi kopyalamasi istenmez.
/// </remarks>
public sealed class AttachmentContent
{
    /// <summary>Ekin ait olacagi kiraci.</summary>
    public required string TenantId { get; init; }

    /// <summary>Ekin baglandigi oturum. Oturumsuz yuklemede <see langword="null"/>.</summary>
    public string? SessionId { get; init; }

    /// <summary>Ekin uretildigi calistirma. Kullanici yuklemesinde <see langword="null"/>.</summary>
    public Guid? RunId { get; init; }

    /// <summary>Orijinal dosya adi.</summary>
    public required string FileName { get; init; }

    /// <summary>Dogrulanmis MIME turu.</summary>
    public required string MediaType { get; init; }

    /// <summary>Ham icerik.</summary>
    public required ReadOnlyMemory<byte> Data { get; init; }

    /// <summary>Yukleyen aktor. Bilinmiyorsa <see langword="null"/>.</summary>
    public string? CreatedBy { get; init; }
}
