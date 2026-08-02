namespace AgentPrism;

/// <summary>Kayitli bir kiraci.</summary>
/// <remarks>
/// Kiraci kaydi <strong>zorunlu degildir</strong>. Diger tablolardaki
/// <c>tenant_id</c> bu tablonun <c>slug</c> degeriyle ayni metindir ancak yabanci
/// anahtarla baglanmaz: kaydi olmayan bir kiraci calisma aninda hata uretmemelidir.
/// Kayit yalnizca arayuzdeki kiraci seciciye ad ve aciklama saglar.
/// </remarks>
public sealed record TenantDescriptor
{
    /// <summary>Kiraci kimligi. Zaman sirali UUID (v7).</summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Kiraci anahtari. Diger tablolardaki <c>tenant_id</c> sutunuyla ayni metindir
    /// ve <see cref="ITenantContext.TenantId"/> bu degeri dondurur.
    /// </summary>
    public required string Slug { get; init; }

    /// <summary>Arayuzde gosterilecek ad.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Olusturulma zamani (UTC).</summary>
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Kiraci kayitlarinin deposu.</summary>
public interface ITenantStore
{
    /// <summary>Kayitli kiracilari anahtara gore sirali listeler.</summary>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kiracilar.</returns>
    ValueTask<IReadOnlyList<TenantDescriptor>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Bir kirayi ekler veya gunceller. Anahtar <see cref="TenantDescriptor.Slug"/> degeridir.</summary>
    /// <param name="tenant">Yazilacak kiraci.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kalici kayit.</returns>
    ValueTask<TenantDescriptor> SaveAsync(TenantDescriptor tenant, CancellationToken cancellationToken = default);

    /// <summary>Bir kiraci kaydini siler. Kiracinin verisi silinmez.</summary>
    /// <param name="slug">Kiraci anahtari.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kayit silindiyse <see langword="true"/>.</returns>
    ValueTask<bool> DeleteAsync(string slug, CancellationToken cancellationToken = default);
}
