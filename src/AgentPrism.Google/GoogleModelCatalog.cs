namespace AgentPrism;

/// <summary>
/// Saglayici ayarlarindaki model tanimlarindan arayuze gosterilecek katalogu kurar.
/// </summary>
/// <remarks>
/// <para>
/// <strong>AgentPrism yerlesik bir model listesi tasimaz</strong> (karar K-032).
/// Bu, Gemini'de somut bir bedel olarak yasandi: olculdu (2026-08-05),
/// <c>gemini-2.5-flash</c> cagrisi <em>"This model is no longer available to new
/// users"</em> dondu. Koda gomulu bir liste yayinlandigi gun bile yanlis olabilir.
/// </para>
/// <para>
/// Katalog <em>bir dogrulama listesi degildir</em>: burada bulunmayan bir model adi
/// da kullanilabilir. Katalog yalnizca arayuzun model secim ekranini ve maliyet
/// hesabini besler.
/// </para>
/// </remarks>
public static class GoogleModelCatalog
{
    /// <summary>Ayarlardaki model tanimlarindan katalogu kurar.</summary>
    /// <param name="options">Saglayici ayarlari.</param>
    /// <returns>Ada gore siralanmis model listesi. Tanim yoksa bos liste.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> <see langword="null"/> ise.</exception>
    /// <remarks>
    /// Ayni ad birden cok kez tanimlanmissa son tanim kazanir. Karsilastirma
    /// buyuk/kucuk harfe duyarli degildir. Adsiz girdiler yok sayilir.
    /// </remarks>
    public static IReadOnlyList<ModelDescriptor> Build(GoogleProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Models.Count == 0)
        {
            return [];
        }

        var byName = new Dictionary<string, ModelDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (var model in options.Models)
        {
            if (model is not null && !string.IsNullOrWhiteSpace(model.Name))
            {
                byName[model.Name] = model;
            }
        }

        var result = byName.Values.ToList();
        result.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

        return result;
    }
}
