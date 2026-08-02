namespace AgentPrism;

/// <summary>Ek ustverisinin ve varsayilan (veritabani) icerik depolamasinin sozlesmesi.</summary>
/// <remarks>
/// <see cref="IAttachmentStorage"/> kayitliysa icerik orada yasar ve bu depo
/// yalnizca ustveriyi tutar; kayitli degilse icerik dogrudan veritabaninda
/// (<c>bytea</c>) saklanir. Gerekce: <c>docs/14-COK-MODLULUK.md</c>, bolum 14.3.
/// </remarks>
public interface IAttachmentStore
{
    /// <summary>Yeni bir ek kaydeder.</summary>
    /// <param name="content">Kaydedilecek icerik ve ustveri.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kimlik ve ozet atanmis ek kaydi.</returns>
    ValueTask<AttachmentDescriptor> SaveAsync(AttachmentContent content, CancellationToken cancellationToken = default);

    /// <summary>Tek bir ekin ustverisini okur.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="id">Ek kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kayit; yoksa veya baska bir kiraciya aitse <see langword="null"/>.</returns>
    ValueTask<AttachmentDescriptor?> GetAsync(string tenantId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Bir ekin ham icerigini akis olarak acar.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="id">Ek kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Okunabilir akis; kayit yoksa veya baska bir kiraciya aitse <see langword="null"/>.</returns>
    ValueTask<Stream?> OpenReadAsync(string tenantId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Ekleri filtreleyerek listeler.</summary>
    /// <param name="query">Filtre.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>En yeniden eskiye sirali kayitlar.</returns>
    ValueTask<IReadOnlyList<AttachmentDescriptor>> ListAsync(AttachmentQuery query, CancellationToken cancellationToken = default);

    /// <summary>Bir eki siler.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="id">Ek kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Bir kayit silindiyse <see langword="true"/>.</returns>
    ValueTask<bool> DeleteAsync(string tenantId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir oturuma ait tum ekleri siler.
    /// </summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="sessionId">Oturum kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Silinen kayit sayisi.</returns>
    /// <remarks>
    /// Bir oturum silindiginde cagrilir; sahipsiz ek birikmesini engeller.
    /// Gerekce: <c>docs/14-COK-MODLULUK.md</c>, acik soru 2.
    /// </remarks>
    ValueTask<int> DeleteBySessionAsync(string tenantId, string sessionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Ek iceriginin harici bir depoda (S3, Blob) saklanmasi icin genisleme noktasi.
/// </summary>
/// <remarks>
/// Kayitli degilse icerik veritabaninda yasar (K1 — sifir surpriz). AgentPrism
/// hicbir bulut SDK'sina bagimlilik almaz; uygulamayi tuketici yazar.
/// Gerekce: <c>docs/KARARLAR.md</c>, K-007.
/// </remarks>
public interface IAttachmentStorage
{
    /// <summary>Icerigi harici depoya yazar.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="id">Ek kimligi.</param>
    /// <param name="content">Yazilacak icerik.</param>
    /// <param name="mediaType">Icerigin MIME turu.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Icerigin harici depodaki konumu.</returns>
    ValueTask<Uri> WriteAsync(
        string tenantId,
        Guid id,
        Stream content,
        string mediaType,
        CancellationToken cancellationToken = default);

    /// <summary>Harici depodan icerigi okur.</summary>
    /// <param name="uri"><see cref="WriteAsync"/> tarafindan dondurulen konum.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Okunabilir akis; konum artik yoksa <see langword="null"/>.</returns>
    ValueTask<Stream?> ReadAsync(Uri uri, CancellationToken cancellationToken = default);

    /// <summary>Harici depodaki icerigi siler.</summary>
    /// <param name="uri"><see cref="WriteAsync"/> tarafindan dondurulen konum.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    ValueTask DeleteAsync(Uri uri, CancellationToken cancellationToken = default);
}
