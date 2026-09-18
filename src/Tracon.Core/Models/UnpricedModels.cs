namespace Tracon;

/// <summary>
/// Finds the catalog models no price source covers.
/// </summary>
/// <remarks>
/// <para>
/// One implementation, two readers: the startup warning
/// (<c>UnpricedModelWarningService</c>) and the diagnostics report. They must
/// never disagree about which models are unpriced — an operator who reads the
/// report to check the warning has to see the same list.
/// </para>
/// <para>
/// The sources are checked in the SAME order <c>RunPricingResolver.Resolve</c>
/// checks them, with the same "either rate is enough" rule: a model priced on
/// input alone is priced, not unpriced.
/// </para>
/// </remarks>
internal static class UnpricedModels
{
    /// <summary>
    /// Lists the <c>provider/model</c> pairs whose price would resolve to
    /// <see cref="PricingSource.Unknown"/>.
    /// </summary>
    /// <param name="providers">The registered providers and their catalogs.</param>
    /// <param name="pricing">The <c>Tracon:Pricing</c> section, checked as the fallback source.</param>
    /// <returns>The pairs, in catalog order. Empty when every model is priced, or when no model is defined at all.</returns>
    public static IReadOnlyList<string> Find(
        IEnumerable<IModelProvider> providers,
        TraconPricingOptions pricing)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(pricing);

        var unpriced = new List<string>();

        foreach (var provider in providers)
        {
            foreach (var model in provider.Models)
            {
                if (IsPriced(provider.Name, model, pricing))
                {
                    continue;
                }

                var pair = $"{provider.Name}/{model.Name}";

                // The same provider can be registered twice — UseOpenAI()
                // registers one instance for ChatCompletions and one for
                // Responses — and the report has one row per pair.
                if (!unpriced.Contains(pair, StringComparer.OrdinalIgnoreCase))
                {
                    unpriced.Add(pair);
                }
            }
        }

        return unpriced;
    }

    private static bool IsPriced(string providerName, ModelDescriptor model, TraconPricingOptions pricing)
    {
        if (model.InputCostPerMillionTokens is not null || model.OutputCostPerMillionTokens is not null)
        {
            return true;
        }

        return pricing.Providers.TryGetValue(providerName, out var configured)
               && configured.TryGetValue(model.Name, out var price)
               && (price.InputCostPerMillionTokens is not null || price.OutputCostPerMillionTokens is not null);
    }
}
