namespace AgentPrism;

/// <summary>
/// Saglayici ayarlarindaki model tanimlarindan arayuze gosterilecek katalogu kurar.
/// </summary>
/// <remarks>
/// <para>
/// <strong>AgentPrism yerlesik bir model listesi tasimaz.</strong> Bu bilincli bir
/// karardir: OpenAI model adlari ve fiyatlari, bir NuGet paketinin yayin sikligindan
/// cok daha hizli degisir. Koda gomulu bir liste kisa surede yaniltici olur.
/// </para>
/// <para>
/// Olculdu (2026-08-02): Faz 3 sirasinda yazilan yerlesik liste, gercek bir hesabin
/// erisebildigi modellerin hicbirini icermiyordu; listedeki <c>gpt-4.1-mini</c>
/// cagrisi <c>HTTP 403 model_not_found</c> dondu. Gerekce:
/// <c>docs/KARARLAR.md</c>, karar K-032.
/// </para>
/// <para>
/// Katalog <see cref="OpenAIProviderOptions.Models"/> ayarindan gelir. Katalog
/// <em>bir dogrulama listesi degildir</em>: burada bulunmayan bir model adi da
/// kullanilabilir, saglayici istegi oldugu gibi OpenAI'a gonderir. Katalog yalnizca
/// arayuzun model secim ekranini ve maliyet hesabini besler.
/// </para>
/// <example>
/// <code language="json">
/// "AgentPrism": { "Providers": { "OpenAI": {
///   "Models": [
///     { "Name": "gpt-5.4-mini", "ContextWindowTokens": 400000, "InputCostPerMillionTokens": 0.25 }
///   ]
/// }}}
/// </code>
/// </example>
/// </remarks>
public static class OpenAIModelCatalog
{
    /// <summary>Ayarlardaki model tanimlarindan katalogu kurar.</summary>
    /// <param name="options">Saglayici ayarlari.</param>
    /// <returns>Ada gore siralanmis model listesi. Tanim yoksa bos liste.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> <see langword="null"/> ise.</exception>
    /// <remarks>
    /// Ayni ad birden cok kez tanimlanmissa son tanim kazanir. Karsilastirma
    /// buyuk/kucuk harfe duyarli degildir. Adsiz girdiler yok sayilir.
    /// </remarks>
    public static IReadOnlyList<ModelDescriptor> Build(OpenAIProviderOptions options)
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
