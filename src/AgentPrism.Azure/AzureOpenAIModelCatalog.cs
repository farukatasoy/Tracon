namespace AgentPrism;

/// <summary>
/// Saglayici ayarlarindaki deployment tanimlarindan arayuze gosterilecek katalogu
/// kurar.
/// </summary>
/// <remarks>
/// <para>
/// <strong>AgentPrism yerlesik bir model listesi tasimaz.</strong> Azure'da bu kural
/// daha da baglayicidir: katalogda yazan ad bir <em>model</em> adi degil, o kaynakta
/// tanimli bir <em>deployment</em> adidir ve deployment adlarini kaynagi kuran kisi
/// secer. Iki AgentPrism tuketicisinin kataloglari birbirine benzemek zorunda
/// degildir. Gerekce: <c>docs/KARARLAR.md</c>, karar K-032.
/// </para>
/// <para>
/// Katalog <em>bir dogrulama listesi degildir</em>: burada bulunmayan bir deployment
/// adi da kullanilabilir. Katalog yalnizca arayuzun model secim ekranini ve maliyet
/// hesabini besler.
/// </para>
/// <example>
/// <code language="json">
/// "AgentPrism": { "Providers": { "AzureOpenAI": {
///   "Models": [
///     { "Name": "uretim-gpt", "DisplayName": "Uretim (gpt-5.4-mini)", "ContextWindowTokens": 128000 }
///   ]
/// }}}
/// </code>
/// </example>
/// </remarks>
public static class AzureOpenAIModelCatalog
{
    /// <summary>Ayarlardaki deployment tanimlarindan katalogu kurar.</summary>
    /// <param name="options">Saglayici ayarlari.</param>
    /// <returns>Ada gore siralanmis liste. Tanim yoksa bos liste.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> <see langword="null"/> ise.</exception>
    /// <remarks>
    /// Ayni ad birden cok kez tanimlanmissa son tanim kazanir. Karsilastirma
    /// buyuk/kucuk harfe duyarli degildir. Adsiz girdiler yok sayilir.
    /// </remarks>
    public static IReadOnlyList<ModelDescriptor> Build(AzureOpenAIProviderOptions options)
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
