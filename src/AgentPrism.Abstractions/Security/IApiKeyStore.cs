namespace AgentPrism;

/// <summary>Kiraci bazli API anahtarlarinin deposu.</summary>
/// <remarks>
/// Faz 53. Statik bearer token'in ikinci, kiraciya baglanan ve kapsam
/// tasiyan bir kimlik kaynagidir; statik token'in yerini ALMAZ. Ayrintili
/// gerekce: docs/53-KIRACI-API-ANAHTARLARI.md.
/// </remarks>
public interface IApiKeyStore
{
    /// <summary>Yeni bir anahtar uretir ve saklar.</summary>
    /// <param name="draft">Anahtarin taslagi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>
    /// Kaydedilen kayit ve ham anahtar deger. Ham deger bu cagridan sonra bir
    /// daha uretilemez (bolum 53.2).
    /// </returns>
    ValueTask<ApiKeyCreationResult> CreateAsync(ApiKeyDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Bir kiracinin anahtarlarini listeler. Ham deger ve ozet DONMEZ.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Anahtarlar, olusturulma zamanina gore.</returns>
    ValueTask<IReadOnlyList<ApiKeyRecord>> ListAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Bir anahtari SHA-256 ozetiyle arar.</summary>
    /// <param name="keyHash">Sunulan ham degerin ozeti.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kayit; yoksa <see langword="null"/>.</returns>
    /// <remarks>
    /// 🚨 Kiraci suzgeci <strong>uygulanmaz</strong>: kiraci bu cagrinin
    /// GIRDISI degil, CIKTISIDIR — bir istegi dogrularken hangi kiraciya ait
    /// oldugunu henuz bilmeyiz (bolum 53.5). Arama her zaman ozet uzerinden
    /// yapilir; ham deger hicbir sorguya dogrudan girmez.
    /// </remarks>
    ValueTask<ApiKeyRecord?> FindByHashAsync(ReadOnlyMemory<byte> keyHash, CancellationToken cancellationToken = default);

    /// <summary>Bir anahtari iptal eder. Satir SILINMEZ; <c>revoked_at</c> yazilir.</summary>
    /// <param name="tenantId">Anahtarin bagli oldugu kiraci.</param>
    /// <param name="id">Anahtar kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Anahtar bu kiracida bulunup iptal edildiyse <see langword="true"/>.</returns>
    ValueTask<bool> RevokeAsync(string tenantId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Son kullanim damgasini gunceller.</summary>
    /// <param name="id">Anahtar kimligi.</param>
    /// <param name="usedAt">Kullanim zamani.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    /// <remarks>
    /// 🚨 Kiraci suzgeci yoktur: cagiran (<c>ApiKeyAuthenticator</c>) anahtari
    /// zaten ozet uzerinden bulmus ve kiraciyi COZMUSTUR; burada ikinci bir
    /// dogrulama gereksizdir — <see cref="FindByHashAsync"/> ile ayni gerekce.
    /// </remarks>
    ValueTask TouchLastUsedAsync(Guid id, DateTimeOffset usedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sistemde (herhangi bir kiracida) verilen kapsami tasiyan, iptal
    /// edilmemis ve suresi gecmemis en az bir anahtar olup olmadigini soyler.
    /// </summary>
    /// <param name="scope">Aranan kapsam.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Bulunursa <see langword="true"/>.</returns>
    /// <remarks>
    /// 🚨 Kiraci suzgeci BILEREK yoktur: bu bir kurulum saglik denetimidir
    /// (<c>ExternalSurfaceGuard</c>, bolum 53.4), belirli bir kiraciya ozgu
    /// degildir — <c>AllowRemoteAccess</c> ile dis yuzeyin BIRLIKTE
    /// acilabilmesi icin sistemde en az bir gecerli <c>external:invoke</c>
    /// anahtari olup olmadigi sorulur.
    /// </remarks>
    ValueTask<bool> HasActiveScopeAsync(ApiKeyScope scope, CancellationToken cancellationToken = default);
}
