using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// <see cref="IRunPricingResolver"/>'in varsayilan uygulamasi. Fiyat sirasi:
/// model kataloğu, sonra <c>AgentPrism:Pricing</c> yapilandirmasi (karar K-032).
/// </summary>
/// <remarks>
/// Saglayici verilmediginde (gecmis bir satirin yeniden hesaplanmasi — <c>runs</c>
/// tablosu saglayici tasimaz) her iki kaynakta da modelin adiyla eslesen
/// <strong>ilk</strong> saglayici kullanilir;
/// <see cref="IModelProviderRegistry.List"/> zaten saglayici adina gore
/// alfabetik siralar. Ayni model adi birden fazla saglayicida farkli fiyatla
/// tanimliysa bu, bilinen ve kabul edilmis bir sinirlamadir (bkz.
/// <c>docs/KARARLAR.md</c> K-154) — <c>runs</c> tablosuna bir saglayici sutunu
/// eklenmedigi surece cozulemez.
/// </remarks>
public sealed class RunPricingResolver : IRunPricingResolver
{
    private readonly IModelProviderRegistry _registry;
    private readonly IOptions<AgentPrismOptions> _options;

    /// <summary>Yeni bir fiyat cozumleyici olusturur.</summary>
    /// <param name="registry">Model kataloğu.</param>
    /// <param name="options">AgentPrism ayarlari (<c>Pricing</c> bolumu).</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
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

        // Saglayici bilinmiyor (yeniden hesaplama): saglayici adlarini
        // alfabetik sirayla dolasip modelin adiyla eslesen ilkini kullan.
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
