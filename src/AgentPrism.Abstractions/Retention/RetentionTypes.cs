namespace AgentPrism;

/// <summary>Bir hedef icin yas ve hacim bazli saklama kurali.</summary>
/// <remarks>
/// Kayit yoksa hedef icin hicbir sey silinmez (bkz. <c>AgentPrismRetentionOptions</c>
/// yapilandirma tabanli varsayilanlari — yalniz veritabaninda kayit YOKSA devreye girer).
/// </remarks>
public sealed record RetentionPolicy
{
    /// <summary>Politika kimligi.</summary>
    public required Guid Id { get; init; }

    /// <summary>Kiraci kimligi. <c>"*"</c> tum kiracilar icin varsayilan anlamina gelir.</summary>
    public required string TenantId { get; init; }

    /// <summary>Hedef tablo adi. Bkz. <see cref="RetentionTargets"/>.</summary>
    public required string Target { get; init; }

    /// <summary>
    /// Bu yastan eski satirlar silinmeye adaydir. <see langword="null"/> ise
    /// yas bazli silme uygulanmaz (yalniz <see cref="MaxRows"/> varsa o gecerlidir).
    /// </summary>
    public int? MaxAgeDays { get; init; }

    /// <summary>
    /// Hedef tabloda tutulacak en fazla satir sayisi. Sinirin uzerindeki en
    /// ESKI satirlar silinir. <see langword="null"/> ise hacim bazli silme
    /// uygulanmaz (yalniz <see cref="MaxAgeDays"/> varsa o gecerlidir).
    /// </summary>
    public long? MaxRows { get; init; }

    /// <summary>
    /// Silmeden once <c>IArchiveSink</c> ile arsivlensin mi. Sink kayitli
    /// degilse bu alan <see langword="true"/> olsa bile hicbir satir silinmez.
    /// </summary>
    public bool Archive { get; init; }

    /// <summary>Politika etkin mi.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Olusturulma zamani (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Son guncelleme zamani (UTC).</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Bir temizleme kosusunun gecmis kaydi.</summary>
public sealed record RetentionRun
{
    /// <summary>Kosu kimligi.</summary>
    public required Guid Id { get; init; }

    /// <summary>Kiraci kimligi.</summary>
    public required string TenantId { get; init; }

    /// <summary>Islenen hedef.</summary>
    public required string Target { get; init; }

    /// <summary>Su ana kadar silinen satir sayisi.</summary>
    public long DeletedRows { get; init; }

    /// <summary>Su ana kadar arsivlenen satir sayisi.</summary>
    public long ArchivedRows { get; init; }

    /// <summary>Baslama zamani (UTC).</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Bitis zamani (UTC). Kosu surerken <see langword="null"/>.</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Hata mesaji. Yalniz basarisiz kosularda dolu.</summary>
    public string? Error { get; init; }
}

/// <summary>Bir hedef icin "su an calistirilirsa kac satir silinir" onizlemesi.</summary>
public sealed record RetentionPreview
{
    /// <summary>Onizlenen hedef.</summary>
    public required string Target { get; init; }

    /// <summary>Politika bulunamadiysa (hicbir sey silinmeyecek) <see langword="null"/>.</summary>
    public int? MaxAgeDays { get; init; }

    /// <summary>Politika etkin mi.</summary>
    public bool Enabled { get; init; }

    /// <summary>Hesaplanan kesim tarihi (UTC). Politika yoksa <see langword="null"/>.</summary>
    public DateTimeOffset? Cutoff { get; init; }

    /// <summary>Kesim tarihinden eski, su an eslesen satir sayisi.</summary>
    public long MatchingRows { get; init; }
}

/// <summary>Arsive yazilacak tek bir satirin JSON temsili.</summary>
/// <remarks>
/// Satirin sema bilgisi tasinmaz: sutun adlari ve degerleri JSON nesnesi
/// icinde dogrudan durur (JSONL'in bir satiri). Boylece arsivleme, hedefin
/// sutun kumesini onceden bilmeye ihtiyac duymaz.
/// </remarks>
public sealed record ArchiveRow
{
    /// <summary>Satirin tek satirlik JSON gosterimi (sonunda satir sonu YOKTUR).</summary>
    public required string Json { get; init; }
}
