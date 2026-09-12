using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Provides the default <see cref="IRunPricingResolver"/> implementation. It
/// resolves prices from the model catalog, then <c>Tracon:Pricing</c> configuration.
/// </summary>
/// <remarks>
/// When no provider is supplied, such as during historical recalculation, the
/// first provider that has the model name is used from either source. The
/// <c>runs</c> table has no provider column and <see cref="IModelProviderRegistry.List"/>
/// already orders providers alphabetically. Different prices for a same-named
/// model across providers are a known, accepted limitation; it
/// cannot be resolved without adding a provider column to <c>runs</c>.
/// </remarks>
internal sealed class RunPricingResolver : IRunPricingResolver
{
    private readonly IModelProviderRegistry _registry;
    private readonly IOptions<TraconOptions> _options;
    private readonly ILogger _logger;

    /// <summary>Initializes a price resolver.</summary>
    /// <param name="registry">The model catalog.</param>
    /// <param name="options">The Tracon options and <c>Pricing</c> section.</param>
    /// <param name="logger">
    /// The logger used to report a usage record that contradicts itself, such as
    /// more cached input tokens than input tokens in total. Optional: the price
    /// is still computed without it.
    /// </param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public RunPricingResolver(
        IModelProviderRegistry registry,
        IOptions<TraconOptions> options,
        ILogger<RunPricingResolver>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(options);

        _registry = registry;
        _options = options;
        _logger = logger ?? NullLogger<RunPricingResolver>.Instance;
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
                catalogPrice.CachedInputCostPerMillionTokens,
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
                configuredPrice.CachedInputCostPerMillionTokens,
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
        TraconPricingOptions pricing,
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

    /// <summary>
    /// Computes the cost, charging cached input tokens at their own rate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The computation is SUBTRACTIVE, because <see cref="RunUsage.CachedInputTokens"/>
    /// is counted INSIDE <see cref="RunUsage.InputTokens"/> (the
    /// Microsoft.Extensions.AI contract). Pricing the whole input at the full
    /// rate and then adding a cache charge would bill the cached tokens twice.
    /// </para>
    /// <para>
    /// Two invariants hold here. When <paramref name="cachedRate"/> is
    /// <see langword="null"/> NOTHING is subtracted and
    /// <see cref="RunCost.CachedInputCost"/> stays <see langword="null"/>: the
    /// result is byte-for-byte the value this method produced before cache
    /// pricing existed. And the run never falls to
    /// <see cref="PricingSource.Unknown"/> for a missing cache rate — that
    /// source means the MODEL price is missing, which is a different fault.
    /// </para>
    /// </remarks>
    private RunCost Compute(
        decimal? inputRate,
        decimal? outputRate,
        decimal? cachedRate,
        RunUsage usage,
        PricingSource source,
        string? currency)
    {
        var inputTokens = usage.InputTokens ?? 0;

        // 🚨 The provider not reporting a cache count is NOT the same as it
        // reporting zero. When nothing was measured, no cache charge is produced
        // at all and CachedInputCost stays null — writing 0m would claim an
        // observed cache miss, exactly the null-not-zero rule RunUsage states.
        // A reported 0 IS a measurement and does produce a 0m charge.
        if (usage.CachedInputTokens is null)
        {
            cachedRate = null;
        }

        var cachedTokens = usage.CachedInputTokens ?? 0;

        // 🚨 More cached tokens than input tokens is a DATA fault, not a rounding
        // artefact: the provider contradicted its own contract. The count is not
        // clamped into shape — the anomaly is logged and the whole input is
        // charged at the full rate, which is the conservative direction (it can
        // over-report, never under-report).
        if (cachedTokens > inputTokens)
        {
            _logger.LogWarning(
                "Usage reports {CachedInputTokens} cached input tokens but only {InputTokens} input tokens in total. " +
                "Cached tokens are counted INSIDE the input total, so this is inconsistent; the cache rate is " +
                "ignored and the whole input is priced at the full input rate.",
                cachedTokens,
                inputTokens);

            cachedTokens = 0;
            cachedRate = null;
        }

        var fullPriceInputTokens = cachedRate is null ? inputTokens : inputTokens - cachedTokens;

        return new RunCost
        {
            InputCost = inputRate is { } inputRateValue ? inputRateValue * fullPriceInputTokens / 1_000_000m : null,
            InputPricePerMillionTokens = inputRate,
            OutputCost = outputRate is { } outputRateValue ? outputRateValue * (usage.OutputTokens ?? 0) / 1_000_000m : null,
            OutputPricePerMillionTokens = outputRate,

            // Reasoning tokens are already inside OutputTokens and are charged at
            // the output rate; providers do not bill them separately today, and an
            // unmeasured distinction is not worth a wider price schema (K-032).
            CachedInputCost = cachedRate is { } cachedRateValue ? cachedRateValue * cachedTokens / 1_000_000m : null,
            CachedInputPricePerMillionTokens = cachedRate,
            Currency = currency,
            Source = source,
        };
    }
}
