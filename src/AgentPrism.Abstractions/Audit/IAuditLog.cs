namespace AgentPrism;

/// <summary>Denetim izi defteri.</summary>
/// <remarks>
/// <para>
/// Yazma <strong>depo dekoratorlerinde</strong> yapilir, uc katmaninda degil: uc
/// katmaninda yazmak, ayni depoya baska bir kod yolundan yapilan degisikligi
/// kacirirdi. Dekorator tek kapidir.
/// </para>
/// <para>
/// <strong>Yazma hatasi islemi kesmez.</strong> <see cref="WriteAsync"/> hata
/// verirse cagiran loglar ve islem devam eder — Faz 6'nin "gozlemlenebilirlik
/// islevi bozmaz" kuralinin aynisi.
/// </para>
/// <para>Sadece okuma ucu vardir; silme veya duzeltme ucu yoktur ve olmayacaktir.</para>
/// </remarks>
public interface IAuditLog
{
    /// <summary>Bir denetim kaydi yazar.</summary>
    /// <param name="entry">Yazilacak kayit.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Kayitlari filtreleyerek okur.</summary>
    /// <param name="query">Filtre.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kayitlar, en yeni basta.</returns>
    ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default);
}
