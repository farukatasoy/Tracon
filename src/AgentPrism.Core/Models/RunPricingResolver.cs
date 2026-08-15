using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Provides the default <see cref="IRunPricingResolver"/> implementation. It
/// resolves prices from the model catalog, then <c>AgentPrism:Pricing</c> configuration (K-032).
/// </summary>
/// <remarks>
/// When no provider is supplied, such as during historical recalculation, the
/// first provider that has the model name is used from either source. The
/// <c>runs</c> table has no provider column and <see cref="IModelProviderRegistry.List"/>
/// already orders providers alphabetically. Different prices for a same-named
/// model across providers are a known, accepted limitation (see K-154); it
/// cannot be resolved without adding a provider column to <c>runs</c>.
/// </remarks>
public sealed class RunPricingResolver : IRunPricingResolver
{
    private readonly IModelProviderRegistry _registry;
    private readonly IOptions<AgentPrismOptions> _options;

    /// <summary>Initializes a price resolver.</summary>
    /// <param name="registry">The model catalog.</param>
    /// <param name="options">The AgentPrism options and <c>Pricing</c> section.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public RunPricingResolver(IModelProviderRegistry registry, IOptions<AgentPrismOptions> options)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(options);

        _registry = registry;
        _options = options;
    }

    /// <inheritdoc />
    public RunCost? Resolve(string? provider, string? model, RunUsage? usage)
    {
        if (model is not { Length: > 0 } || usage is null)
        {
            return null;
        }

        var pricing = _options.Value.Pricing;
        var currency = pricing.Currency;

        var catalogPrice = FindCatalogPrice(provider, model);
        if (catalogPrice is { InputCostPerMillionTokens: not null } or { OutputCostPerMillionTokens: not null })
        {
            return Compute(
                catalogPrice!.InputCostPerMillionTokens,
                catalogPrice.OutputCostPerMillionTokens,
                usage,
                PricingSource.Catalog,
                currency);
        }

        var configuredPrice = FindConfiguredPrice(pricing, provider, model);
        if (configuredPrice is { InputCostPerMillionTokens: not null } or { OutputCostPerMillionTokens: not null })
        {
            return Compute(
                configuredPrice!.InputCostPerMillionTokens,
                configuredPrice.OutputCostPerMillionTokens,
                usage,
                PricingSource.Configuration,
                currency);
        }

        return new RunCost { Source = PricingSource.Unknown };
    }

    private ModelDescriptor? FindCatalogPrice(string? provider, string model)
    {
        foreach (var providerDescriptor in _registry.List())
        {
            if (provider is { Length: > 0 } &&
                !string.Equals(providerDescriptor.Name, provider, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var descriptor in providerDescriptor.Models)
            {
                if (string.Equals(descriptor.Name, model, StringComparison.OrdinalIgnoreCase))
                {
                    return descriptor;
                }
            }
        }

        return null;
    }

    private static ModelPriceOverride? FindConfiguredPrice(
        AgentPrismPricingOptions pricing,
        string? provider,
        string model)
    {
        if (provider is { Length: > 0 })
        {
            return pricing.Providers.TryGetValue(provider, out var models) &&
                   models.TryGetValue(model, out var configured)
                ? configured
                : null;
        }

        // The provider is unknown during recalculation. Visit provider names in
        // alphabetical order and use the first one that contains the model name.
        foreach (var providerName in pricing.Providers.Keys.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase))
        {
            if (pricing.Providers[providerName].TryGetValue(model, out var configured))
            {
                return configured;
            }
        }

        return null;
    }

    private static RunCost Compute(
        decimal? inputRate,
        decimal? outputRate,
        RunUsage usage,
        PricingSource source,
        string? currency)
        => new()
        {
            InputCost = inputRate is { } inputRateValue ? inputRateValue * (usage.InputTokens ?? 0) / 1_000_000m : null,
            OutputCost = outputRate is { } outputRateValue ? outputRateValue * (usage.OutputTokens ?? 0) / 1_000_000m : null,
            Currency = currency,
            Source = source,
        };
}
