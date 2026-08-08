namespace AgentPrism;

/// <summary>
/// Bir API anahtarinin veritabaninda saklanan, HAM DEGER TASIMAYAN gorunumu.
/// </summary>
/// <remarks>
/// 🚨 Bu kayitta ham anahtar <strong>yoktur</strong>. Yalnizca geri
/// donduruleyemez bir SHA-256 ozeti (<see cref="KeyPrefix"/> disinda hicbir
/// yerde) veritabaninda durur; ham deger yalnizca olusturma aninda
/// <see cref="ApiKeyCreationResult.PlaintextKey"/> ile <strong>bir kez</strong>
/// doner (docs/53-KIRACI-API-ANAHTARLARI.md, bolum 53.2).
/// </remarks>
public sealed record ApiKeyRecord
{
    /// <summary>Anahtar kimligi.</summary>
    public required Guid Id { get; init; }

    /// <summary>Anahtarin bagli oldugu kiraci. Dogrulandiginda kiraci BURADAN cozulur.</summary>
    public required string TenantId { get; init; }

    /// <summary>Operatorun anahtari tanimasi icin ad.</summary>
    public required string Name { get; init; }

    /// <summary>Ham degerin ilk karakterleri; listede anahtari ayirt etmek icindir.</summary>
    public required string KeyPrefix { get; init; }

    /// <summary>Kapsam kumesi. Etkili yetki <c>rol ∩ kapsam</c>'dir.</summary>
    public required IReadOnlyList<ApiKeyScope> Scopes { get; init; }

    /// <summary>Sure sonu. <see langword="null"/> ise suresizdir.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Iptal damgasi. Satir SILINMEZ; denetim izi bu alanla korunur.</summary>
    public DateTimeOffset? RevokedAt { get; init; }

    /// <summary>Son kullanim zamani. Kullanilmayan anahtari gormek icindir.</summary>
    public DateTimeOffset? LastUsedAt { get; init; }

    /// <summary>Olusturulma zamani (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Anahtar iptal edilmemis ve suresi gecmemisse <see langword="true"/>.</summary>
    /// <remarks>
    /// Yalnizca GORUNTULEME icindir (arayuz listesi, <c>GET /api/api-keys</c>).
    /// Gercek dogrulama karari <c>ApiKeyAuthenticator</c>'da enjekte edilmis bir
    /// <see cref="TimeProvider"/> ile verilir; burada <see cref="DateTimeOffset.UtcNow"/>
    /// kullanilmasi bir guvenlik karari degildir ve test edilebilirligi etkilemez.
    /// </remarks>
    public bool IsActive => RevokedAt is null && (ExpiresAt is null || ExpiresAt > DateTimeOffset.UtcNow);
}

/// <summary>Bir anahtar olusturma isleminin sonucu.</summary>
/// <remarks>Ham anahtar YALNIZCA burada, olusturma aninda doner (bolum 53.2).</remarks>
public sealed record ApiKeyCreationResult
{
    /// <summary>Kaydedilen, ham deger tasimayan gorunum.</summary>
    public required ApiKeyRecord Record { get; init; }

    /// <summary>
    /// Ham anahtar degeri. Bu cagridan sonra bir daha uretilemez; tuketici
    /// bunu hemen gostermeli ve saklamamalidir.
    /// </summary>
    public required string PlaintextKey { get; init; }
}
