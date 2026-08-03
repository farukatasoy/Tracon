namespace AgentPrism;

/// <summary>
/// Bir model+kullanim ciftinden maliyet hesaplar. Fiyat sirasi: model
/// katalogu, sonra <c>AgentPrism:Pricing</c> yapilandirmasi (K-032).
/// </summary>
public interface IRunPricingResolver
{
    /// <summary>Maliyeti coz.</summary>
    /// <param name="provider">
    /// Saglayici adi. <see langword="null"/> ise (ornek: gecmis bir satirin
    /// yeniden hesaplanmasi) fiyat yalniz model adiyla, saglayicilar arasinda
    /// ilk eslesen ile cozulur.
    /// </param>
    /// <param name="model">Model adi. <see langword="null"/> veya bossa maliyet uygulanmaz.</param>
    /// <param name="usage">Token kullanimi. <see langword="null"/> ise maliyet uygulanmaz.</param>
    /// <returns>
    /// <see langword="null"/> yalniz <paramref name="model"/> veya <paramref name="usage"/>
    /// yoksa (fiyatin hic uygulanamayacagi durum — ornegin model baglanmamis bir kod agent'i).
    /// Model biliniyorsa fiyat tanimsiz da olsa <see cref="RunCost"/> her zaman doner;
    /// bu durumda <see cref="RunCost.Source"/> <see cref="PricingSource.Unknown"/>'dir ve
    /// maliyet alanlari <see langword="null"/>'dur — <strong>sifir degil</strong>.
    /// </returns>
    RunCost? Resolve(string? provider, string? model, RunUsage? usage);
}
