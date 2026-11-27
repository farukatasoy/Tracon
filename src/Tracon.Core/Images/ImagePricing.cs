using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Computes image-generation measurements and configured costs.</summary>
/// <remarks>
/// Tracon never invents an image price. An unknown provider, model, or
/// unavailable token count leaves <see cref="ImageUsageMeasurement.Cost"/> null.
/// </remarks>
internal sealed class ImagePricing
{
    private readonly IOptions<TraconOptions> _options;
    private readonly UnpricedMediaWarnings? _warnings;

    public ImagePricing(IOptions<TraconOptions> options, UnpricedMediaWarnings? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _warnings = warnings;
    }

    /// <summary>Gets the configured currency label.</summary>
    public string? Currency => _options.Value.Pricing.Currency;

    /// <summary>Builds one durable measurement for an image-generation response.</summary>
    public ImageUsageMeasurement Measure(
        string provider,
        string model,
        string? size,
        int imageCount,
        long? outputTokens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentOutOfRangeException.ThrowIfNegative(imageCount);

        var price = Find(provider, model);

        // A model with no entry, or an entry with neither rate, is recorded without a cost; say so once. A model that has a
        // rate but no usable measure for this call (a token rate and no token count) is priced and says nothing.
        if (!MediaPriceLookup.IsPriced(MediaPriceKind.Image, _options.Value.Pricing, provider, model))
        {
            _warnings?.WarnOnce(MediaPriceKind.Image, provider, model);
        }

        if (price?.OutputCostPerMillionTokens is { } tokenRate && outputTokens is { } tokens)
        {
            return new ImageUsageMeasurement(
                ToolUsageUnits.Tokens,
                tokens,
                tokenRate * tokens / 1_000_000m,
                Currency);
        }

        if (price?.PerImage is { } imageRate)
        {
            var multiplier = size is not null && price.SizeMultipliers.TryGetValue(size, out var configured)
                ? configured
                : 1m;

            return new ImageUsageMeasurement(
                ToolUsageUnits.Images,
                imageCount,
                imageRate * imageCount * multiplier,
                Currency);
        }

        // A token price is configured but the provider supplied no output token
        // count. Do not write a fabricated zero-token measurement. The produced
        // image count is still a real observation, but its cost is unknown.
        if (price?.OutputCostPerMillionTokens is not null)
        {
            return new ImageUsageMeasurement(ToolUsageUnits.Images, imageCount, null, Currency);
        }

        // Without a configured price, preserve the richer provider measurement
        // when it exists. Cost remains null in either branch.
        return outputTokens is { } measuredTokens
            ? new ImageUsageMeasurement(ToolUsageUnits.Tokens, measuredTokens, null, Currency)
            : new ImageUsageMeasurement(ToolUsageUnits.Images, imageCount, null, Currency);
    }

    private ImagePriceOverride? Find(string provider, string model)
        => MediaPriceLookup.Image(_options.Value.Pricing, provider, model);
}

/// <summary>One image-generation quantity and its configured cost.</summary>
internal sealed record ImageUsageMeasurement(string Unit, decimal Quantity, decimal? Cost, string? Currency);
