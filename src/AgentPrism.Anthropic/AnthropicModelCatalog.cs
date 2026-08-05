namespace AgentPrism;

/// <summary>
/// Saglayici ayarlarindaki model tanimlarindan arayuze gosterilecek katalogu kurar.
/// </summary>
/// <remarks>
/// <para>
/// <strong>AgentPrism yerlesik bir model listesi tasimaz.</strong> Model adlari ve
/// fiyatlari bir NuGet paketinin yayin sikligindan cok daha hizli degisir; koda
/// gomulu bir liste kisa surede yaniltici olur. Gerekce: <c>docs/KARARLAR.md</c>,
/// karar K-032.
/// </para>
/// <para>
/// Katalog <em>bir dogrulama listesi degildir</em>: burada bulunmayan bir model adi
/// da kullanilabilir, saglayici istegi oldugu gibi Anthropic'e gonderir. Katalog
/// yalnizca arayuzun model secim ekranini ve maliyet hesabini besler.
/// </para>
/// <example>
/// <code language="json">
/// "AgentPrism": { "Providers": { "Anthropic": {
///   "Models": [
///     { "Name": "claude-sonnet-5", "ContextWindowTokens": 200000, "InputCostPerMillionTokens": 3 }
///   ]
/// }}}
/// </code>
/// </example>
/// </remarks>
public static class AnthropicModelCatalog
{
    /// <summary>Ayarlardaki model tanimlarindan katalogu kurar.</summary>
    /// <param name="options">Saglayici ayarlari.</param>
    /// <returns>Ada gore siralanmis model listesi. Tanim yoksa bos liste.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> <see langword="null"/> ise.</exception>
    /// <remarks>
    /// Ayni ad birden cok kez tanimlanmissa son tanim kazanir. Karsilastirma
    /// buyuk/kucuk harfe duyarli degildir. Adsiz girdiler yok sayilir.
    /// </remarks>
    public static IReadOnlyList<ModelDescriptor> Build(AnthropicProviderOptions options)
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
