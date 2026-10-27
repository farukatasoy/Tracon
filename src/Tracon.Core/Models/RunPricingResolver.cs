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
                new Rates(
                    catalogPrice!.InputCostPerMillionTokens,
                    catalogPrice.OutputCostPerMillionTokens,
                    catalogPrice.CachedInputCostPerMillionTokens,
                    catalogPrice.CacheWriteCostPerMillionTokens,
                    catalogPrice.CacheWriteLongCostPerMillionTokens),
                usage,
                PricingSource.Catalog,
                currency);
        }

        var configuredPrice = FindConfiguredPrice(pricing, provider, model);
        if (configuredPrice is { InputCostPerMillionTokens: not null } or { OutputCostPerMillionTokens: not null })
        {
            return Compute(
                new Rates(
                    configuredPrice!.InputCostPerMillionTokens,
                    configuredPrice.OutputCostPerMillionTokens,
                    configuredPrice.CachedInputCostPerMillionTokens,
                    configuredPrice.CacheWriteCostPerMillionTokens,
                    configuredPrice.CacheWriteLongCostPerMillionTokens),
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
    /// Computes the cost, charging cached input tokens and cache-written input tokens at their
    /// own rates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The computation is SUBTRACTIVE, because <see cref="RunUsage.CachedInputTokens"/> and the two
    /// cache-write counters are counted INSIDE <see cref="RunUsage.InputTokens"/> (the
    /// Microsoft.Extensions.AI contract). Pricing the whole input at the full
    /// rate and then adding a cache charge would bill those tokens twice. The four parts are
    /// the full-priced input (input − cached − priced writes), the cache read, the cache write
    /// (short tokens at the short rate plus long tokens at the long rate) and the output.
    /// </para>
    /// <para>
    /// Two invariants hold here. When a rate is <see langword="null"/> NOTHING is subtracted for
    /// it and its cost field stays <see langword="null"/>: the result is byte-for-byte the value
    /// this method produced before that rate existed. A cache write is priced class by class, so
    /// a time to live with a rate and a time to live without one can sit in the same run. And the
    /// run never falls to <see cref="PricingSource.Unknown"/> for a missing cache rate — that
    /// source means the MODEL price is missing, which is a different fault.
    /// </para>
    /// </remarks>
    private RunCost Compute(Rates rates, RunUsage usage, PricingSource source, string? currency)
    {
        var inputTokens = usage.InputTokens ?? 0;
        var cachedRate = rates.CachedInput;

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

        var pricedCachedTokens = cachedRate is null ? 0 : cachedTokens;

        // A write is priced only when its class has a rate AND the provider reported a count:
        // a class without a rate keeps its tokens in the full-priced input (253.3).
        var shortWriteTokens = rates.CacheWrite is null ? null : usage.CacheWriteInputTokens;
        var longWriteTokens = rates.CacheWriteLong is null ? null : usage.CacheWriteLongInputTokens;
        var pricedWriteTokens = (shortWriteTokens ?? 0) + (longWriteTokens ?? 0);

        // 🚨 The same data fault for writes: cached and written tokens are both INSIDE the input
        // total, so together they cannot exceed it. The write rates are ignored, not clamped, and
        // the written tokens stay in the full-priced input.
        if (pricedWriteTokens > inputTokens - pricedCachedTokens)
        {
            _logger.LogWarning(
                "Usage reports {WrittenTokens} cache-written input tokens and {CachedInputTokens} cached input tokens " +
                "but only {InputTokens} input tokens in total. Both are counted INSIDE the input total, so this is " +
                "inconsistent; the cache-write rates are ignored and the written tokens are priced at the full input rate.",
                pricedWriteTokens,
                pricedCachedTokens,
                inputTokens);

            shortWriteTokens = null;
            longWriteTokens = null;
            pricedWriteTokens = 0;
        }

        var fullPriceInputTokens = inputTokens - pricedCachedTokens - pricedWriteTokens;

        decimal? cacheWriteCost = null;

        if (shortWriteTokens is { } shortCount)
        {
            cacheWriteCost = rates.CacheWrite!.Value * shortCount / 1_000_000m;
        }

        if (longWriteTokens is { } longCount)
        {
            cacheWriteCost = (cacheWriteCost ?? 0m) + (rates.CacheWriteLong!.Value * longCount / 1_000_000m);
        }

        return new RunCost
        {
            InputCost = rates.Input is { } inputRateValue ? inputRateValue * fullPriceInputTokens / 1_000_000m : null,
            InputPricePerMillionTokens = rates.Input,
            OutputCost = rates.Output is { } outputRateValue ? outputRateValue * (usage.OutputTokens ?? 0) / 1_000_000m : null,
            OutputPricePerMillionTokens = rates.Output,

            // Reasoning tokens are already inside OutputTokens and are charged at
            // the output rate; providers do not bill them separately today, and an
            // unmeasured distinction is not worth a wider price schema (K-032).
            CachedInputCost = cachedRate is { } cachedRateValue ? cachedRateValue * cachedTokens / 1_000_000m : null,
            CachedInputPricePerMillionTokens = cachedRate,
            CacheWriteCost = cacheWriteCost,
            CacheWritePricePerMillionTokens = shortWriteTokens is null ? null : rates.CacheWrite,
            CacheWriteLongPricePerMillionTokens = longWriteTokens is null ? null : rates.CacheWriteLong,
            Currency = currency,
            Source = source,
        };
    }

    /// <summary>The five per-million rates of one model, wherever they were read from.</summary>
    private readonly record struct Rates(
        decimal? Input,
        decimal? Output,
        decimal? CachedInput,
        decimal? CacheWrite,
        decimal? CacheWriteLong);
}
