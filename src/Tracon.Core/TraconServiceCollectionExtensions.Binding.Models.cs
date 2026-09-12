using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Tracon;

public static partial class TraconServiceCollectionExtensions
{
    /// <summary>
    /// Binds the <c>Tracon:Pricing</c> section. The <c>Currency</c>,
    /// <c>Voice</c>, and <c>Images</c> keys are reserved; every other child is
    /// read as a provider name.
    /// </summary>
    private static void BindPricing(IConfigurationSection section, TraconPricingOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (section[nameof(TraconPricingOptions.Currency)] is { Length: > 0 } currency)
        {
            options.Currency = currency;
        }

        BindVoicePricing(section.GetSection(nameof(TraconPricingOptions.Voice)), options);
        BindImagePricing(section.GetSection(nameof(TraconPricingOptions.Images)), options);

        foreach (var providerSection in section.GetChildren())
        {
            // Reserved keys. Since the section is bound by hand, this list is
            // the SINGLE source of truth; a forgotten key is mistaken for a provider name.
            if (string.Equals(
                    providerSection.Key,
                    nameof(TraconPricingOptions.Currency),
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    providerSection.Key,
                    nameof(TraconPricingOptions.Voice),
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    providerSection.Key,
                    nameof(TraconPricingOptions.Images),
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var models = new Dictionary<string, ModelPriceOverride>(StringComparer.OrdinalIgnoreCase);

            foreach (var modelSection in providerSection.GetChildren())
            {
                var input = ReadDecimal(modelSection, "Input");
                var output = ReadDecimal(modelSection, "Output");

                // "CachedInput" is OPTIONAL and, unlike Input/Output, its absence
                // is NOT a configuration fault: a model without a cache rate keeps
                // pricing its cached tokens at the plain input rate. It therefore
                // does NOT take part in the "at least one value" check below.
                var cachedInput = ReadDecimal(modelSection, "CachedInput");

                // 🚨 The record is ADDED even when both are null (K-034): a
                // price entry written with a key name other than
                // "Input"/"Output" (e.g. the C# property name
                // "InputCostPerMillionTokens") therefore does NOT get dropped
                // COMPLETELY SILENTLY - it still enters Providers as an empty
                // ModelPriceOverride with both fields null, and
                // TraconOptionsValidator.ValidatePricing rejects it at
                // startup (MT-CORE-065).
                models[modelSection.Key] = new ModelPriceOverride
                {
                    InputCostPerMillionTokens = input,
                    OutputCostPerMillionTokens = output,
                    CachedInputCostPerMillionTokens = cachedInput,
                };
            }

            if (models.Count > 0)
            {
                options.Providers[providerSection.Key] = models;
            }
        }
    }

    /// <summary>
    /// Binds the <c>Tracon:Pricing:Voice</c> section.
    /// </summary>
    /// <remarks>
    /// Voice pricing is based on characters (generation) or duration
    /// (resolution), not tokens; this is why it is written to a separate
    /// dictionary and not summed together with token prices.
    /// </remarks>
    private static void BindVoicePricing(IConfigurationSection section, TraconPricingOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        foreach (var providerSection in section.GetChildren())
        {
            var models = new Dictionary<string, VoicePriceOverride>(StringComparer.OrdinalIgnoreCase);

            foreach (var modelSection in providerSection.GetChildren())
            {
                var perMillionCharacters = ReadDecimal(
                    modelSection,
                    nameof(VoicePriceOverride.PerMillionCharacters));

                var perMinute = ReadDecimal(modelSection, nameof(VoicePriceOverride.PerMinute));

                // 🚨 Same rationale: see the comment inside BindPricing (MT-CORE-065).
                models[modelSection.Key] = new VoicePriceOverride
                {
                    PerMillionCharacters = perMillionCharacters,
                    PerMinute = perMinute,
                };
            }

            if (models.Count > 0)
            {
                options.Voice[providerSection.Key] = models;
            }
        }
    }

    /// <summary>Binds the <c>Tracon:Pricing:Images</c> section.</summary>
    private static void BindImagePricing(IConfigurationSection section, TraconPricingOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        foreach (var providerSection in section.GetChildren())
        {
            var models = new Dictionary<string, ImagePriceOverride>(StringComparer.OrdinalIgnoreCase);

            foreach (var modelSection in providerSection.GetChildren())
            {
                var price = new ImagePriceOverride
                {
                    PerImage = ReadDecimal(modelSection, nameof(ImagePriceOverride.PerImage)),
                    OutputCostPerMillionTokens = ReadDecimal(
                        modelSection,
                        nameof(ImagePriceOverride.OutputCostPerMillionTokens)),
                };

                foreach (var multiplierSection in modelSection
                    .GetSection(nameof(ImagePriceOverride.SizeMultipliers))
                    .GetChildren())
                {
                    price.SizeMultipliers[multiplierSection.Key] = decimal.TryParse(
                        multiplierSection.Value,
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var multiplier)
                        ? multiplier
                        : -1m;
                }

                // Keep an empty record. The validator must detect a misspelled
                // price key instead of silently making image generation unpriced.
                models[modelSection.Key] = price;
            }

            if (models.Count > 0)
            {
                options.Images[providerSection.Key] = models;
            }
        }
    }

    /// <summary>Binds the <c>Tracon:Images</c> section without reflection.</summary>
    private static void BindImages(IConfigurationSection section, TraconImageOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (bool.TryParse(section[nameof(TraconImageOptions.Enabled)], out var enabled))
        {
            options.Enabled = enabled;
        }

        if (section[nameof(TraconImageOptions.Provider)] is { Length: > 0 } provider)
        {
            options.Provider = provider;
        }

        if (section[nameof(TraconImageOptions.Model)] is { Length: > 0 } model)
        {
            options.Model = model;
        }

        if (int.TryParse(
                section[nameof(TraconImageOptions.MaxImagesPerRequest)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxImages))
        {
            options.MaxImagesPerRequest = maxImages;
        }
    }

    private static decimal? ReadDecimal(IConfiguration section, string key)
        => decimal.TryParse(section[key], NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>
    /// Reads the utility model binding from configuration.
    /// </summary>
    /// <remarks>
    /// <see cref="ModelBinding.Provider"/> and <see cref="ModelBinding.Model"/>
    /// are required; when both are not set, no binding is built. A partially
    /// filled binding would silently accept a configuration that was
    /// mistakenly written incomplete.
    /// </remarks>
    private static ModelBinding? BindUtilityModel(IConfigurationSection section)
    {
        if (!section.Exists())
        {
            return null;
        }

        var provider = section[nameof(ModelBinding.Provider)];
        var model = section[nameof(ModelBinding.Model)];

        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        float.TryParse(section[nameof(ModelBinding.Temperature)], NumberStyles.Float, CultureInfo.InvariantCulture, out var temperature);
        int.TryParse(section[nameof(ModelBinding.MaxOutputTokens)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxOutputTokens);
        float.TryParse(section[nameof(ModelBinding.TopP)], NumberStyles.Float, CultureInfo.InvariantCulture, out var topP);

        return new ModelBinding
        {
            Provider = provider,
            Model = model,
            Temperature = section[nameof(ModelBinding.Temperature)] is { Length: > 0 } ? temperature : null,
            MaxOutputTokens = section[nameof(ModelBinding.MaxOutputTokens)] is { Length: > 0 } ? maxOutputTokens : null,
            TopP = section[nameof(ModelBinding.TopP)] is { Length: > 0 } ? topP : null,
            ReasoningEffort = section[nameof(ModelBinding.ReasoningEffort)],
        };
    }

    private static void BindAgentGraph(IConfigurationSection section, TraconAgentGraphOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (int.TryParse(
                section[nameof(TraconAgentGraphOptions.MaxDepth)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxDepth))
        {
            options.MaxDepth = maxDepth;
        }

        if (long.TryParse(
                section[nameof(TraconAgentGraphOptions.MaxTotalTokens)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxTokens))
        {
            options.MaxTotalTokens = maxTokens;
        }

        if (ReadDecimal(section, nameof(TraconAgentGraphOptions.MaxTotalCost)) is { } maxCost)
        {
            options.MaxTotalCost = maxCost;
        }

        if (int.TryParse(
                section[nameof(TraconAgentGraphOptions.MaxTotalRuns)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxRuns))
        {
            options.MaxTotalRuns = maxRuns;
        }

        if (TimeSpan.TryParse(
                section[nameof(TraconAgentGraphOptions.MaxDuration)],
                CultureInfo.InvariantCulture,
                out var maxDuration))
        {
            options.MaxDuration = maxDuration;
        }

        if (TimeSpan.TryParse(
                section[nameof(TraconAgentGraphOptions.ChildDeadline)],
                CultureInfo.InvariantCulture,
                out var childDeadline))
        {
            options.ChildDeadline = childDeadline;
        }

        if (TimeSpan.TryParse(
                section[nameof(TraconAgentGraphOptions.WaitTimeout)],
                CultureInfo.InvariantCulture,
                out var waitTimeout))
        {
            options.WaitTimeout = waitTimeout;
        }
    }

    private static void BindAttachments(IConfigurationSection section, TraconAttachmentOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (long.TryParse(
                section[nameof(TraconAttachmentOptions.MaxBytes)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxBytes))
        {
            options.MaxBytes = maxBytes;
        }

        var allowed = section.GetSection(nameof(TraconAttachmentOptions.AllowedMediaTypes))
            .GetChildren()
            .Select(static child => child.Value)
            .Where(static value => value is { Length: > 0 })
            .ToArray();

        if (allowed.Length > 0)
        {
            options.AllowedMediaTypes.Clear();

            foreach (var mediaType in allowed)
            {
                options.AllowedMediaTypes.Add(mediaType!);
            }
        }
    }
}
