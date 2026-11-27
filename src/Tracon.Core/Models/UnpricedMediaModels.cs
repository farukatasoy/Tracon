namespace Tracon;

/// <summary>One media model the host's own configuration says it will use.</summary>
/// <param name="Kind">The kind of call the model answers.</param>
/// <param name="Provider">The provider name the price table is keyed by.</param>
/// <param name="Model">The model name the price table is keyed by.</param>
internal readonly record struct ConfiguredMediaModel(MediaPriceKind Kind, string Provider, string Model);

/// <summary>
/// Names the speech, transcription and image models the host configured, so a report can ask whether each one has a price.
/// </summary>
/// <remarks>
/// <para>
/// A source reads the host's <strong>options</strong> and nothing else. It never reads a saved definition, a store or a
/// tenant's binding: those belong to a tenant, a full scan of them has an unknown cost, and the once-per-model warning that
/// fires when a call finds no price already covers them.
/// </para>
/// <para>
/// The package that owns a model setting registers its own source (<c>Tracon.Voice</c> for the speech models, the core for the
/// image models), because the core cannot read an option type that lives in a package it does not reference.
/// </para>
/// </remarks>
internal interface IConfiguredMediaModelSource
{
    /// <summary>Lists the media models this source's options configure.</summary>
    /// <returns>The models; empty when the feature is off or sets none.</returns>
    IReadOnlyList<ConfiguredMediaModel> ConfiguredModels();
}

/// <summary>
/// Finds the configured media models no price covers.
/// </summary>
/// <remarks>
/// One implementation, two readers: the startup warning (<c>UnpricedModelWarningService</c>) and the diagnostics report. They
/// must never disagree about which models are unpriced, for the reason <see cref="UnpricedModels"/> gives. The price rule is
/// <see cref="MediaPriceLookup.IsPriced"/>, the one the calls themselves use.
/// </remarks>
internal static class UnpricedMediaModels
{
    /// <summary>Lists the configured media models whose price would be missing.</summary>
    /// <param name="sources">The sources of configured models.</param>
    /// <param name="pricing">The <c>Tracon:Pricing</c> section.</param>
    /// <returns>
    /// One <c>provider/model (kind)</c> entry for each unpriced model, ordered by kind, provider and model. Empty when every
    /// configured model is priced or none is configured.
    /// </returns>
    public static IReadOnlyList<string> Find(IEnumerable<IConfiguredMediaModelSource> sources, TraconPricingOptions pricing)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(pricing);

        return
        [
            .. sources
                .SelectMany(static source => source.ConfiguredModels())
                .Where(model => !MediaPriceLookup.IsPriced(model.Kind, pricing, model.Provider, model.Model))
                .Distinct()
                .OrderBy(static model => model.Kind)
                .ThenBy(static model => model.Provider, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static model => model.Model, StringComparer.OrdinalIgnoreCase)
                .Select(static model => $"{model.Provider}/{model.Model} ({MediaPriceLookup.Label(model.Kind)})"),
        ];
    }
}
