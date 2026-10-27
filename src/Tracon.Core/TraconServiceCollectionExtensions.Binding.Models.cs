using Microsoft.Extensions.Configuration;

namespace Tracon;

public static partial class TraconServiceCollectionExtensions
{
    /// <summary>
    /// Binds the <c>Tracon:Pricing</c> section. The <c>Currency</c>,
    /// <c>Voice</c>, <c>Images</c>, and <c>HostedTools</c> keys are reserved;
    /// every other child is read as a provider name.
    /// </summary>
    private static void BindPricing(IConfigurationSection section, TraconPricingOptions options, TraconOptions root)
    {
        if (!section.Exists())
        {
            return;
        }

        if (section[nameof(TraconPricingOptions.Currency)] is { Length: > 0 } currency)
        {
            options.Currency = currency;
        }

        BindVoicePricing(section.GetSection(nameof(TraconPricingOptions.Voice)), options, root);
        BindImagePricing(section.GetSection(nameof(TraconPricingOptions.Images)), options, root);
        BindHostedToolPricing(section.GetSection(nameof(TraconPricingOptions.HostedTools)), options, root);

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
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    providerSection.Key,
                    nameof(TraconPricingOptions.HostedTools),
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var models = new Dictionary<string, ModelPriceOverride>(StringComparer.OrdinalIgnoreCase);

            foreach (var modelSection in providerSection.GetChildren())
            {
                var batch = ConfigurationValueReader.For(modelSection, options, root).Batch();
                var input = batch.Decimal(modelSection, "Input");
                var output = batch.Decimal(modelSection, "Output");

                // "CachedInput" is OPTIONAL and, unlike Input/Output, its absence
                // is NOT a configuration fault: a model without a cache rate keeps
                // pricing its cached tokens at the plain input rate. It therefore
                // does NOT take part in the "at least one value" check below.
                var cachedInput = batch.Decimal(modelSection, "CachedInput");

                // "CacheWrite" and "CacheWriteLong" are optional the same way: the
                // provider bills a cache write by how long the entry lives, so each
                // time-to-live class has its own rate, and a class with no rate keeps
                // its tokens at the plain input rate.
                var cacheWrite = batch.Decimal(modelSection, "CacheWrite");
                var cacheWriteLong = batch.Decimal(modelSection, "CacheWriteLong");

                // 🚨 The record is ADDED even when both are null (K-034): a
                // price entry written with a key name other than
                // "Input"/"Output" (e.g. the C# property name
                // "InputCostPerMillionTokens") therefore does NOT get dropped
                // COMPLETELY SILENTLY - it still enters Providers as an empty
                // ModelPriceOverride with both fields null, and
                // TraconOptionsValidator.ValidatePricing rejects it at
                // startup (MT-CORE-065).
                var price = new ModelPriceOverride
                {
                    InputCostPerMillionTokens = input,
                    OutputCostPerMillionTokens = output,
                    CachedInputCostPerMillionTokens = cachedInput,
                    CacheWriteCostPerMillionTokens = cacheWrite,
                    CacheWriteLongCostPerMillionTokens = cacheWriteLong,
                };

                models[modelSection.Key] = price;

                var providerName = providerSection.Key;
                var modelName = modelSection.Key;

                batch.Commit(() => ReferenceEquals(PriceOf(options.Providers, providerName, modelName), price));
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
    private static void BindVoicePricing(IConfigurationSection section, TraconPricingOptions options, TraconOptions root)
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
                var batch = ConfigurationValueReader.For(modelSection, options, root).Batch();
                var perMillionCharacters = batch.Decimal(
                    modelSection,
                    nameof(VoicePriceOverride.PerMillionCharacters));

                var perMinute = batch.Decimal(modelSection, nameof(VoicePriceOverride.PerMinute));

                // 🚨 Same rationale: see the comment inside BindPricing (MT-CORE-065).
                var price = new VoicePriceOverride
                {
                    PerMillionCharacters = perMillionCharacters,
                    PerMinute = perMinute,
                };

                models[modelSection.Key] = price;

                var providerName = providerSection.Key;
                var modelName = modelSection.Key;

                batch.Commit(() => ReferenceEquals(PriceOf(options.Voice, providerName, modelName), price));
            }

            if (models.Count > 0)
            {
                options.Voice[providerSection.Key] = models;
            }
        }
    }

    /// <summary>Binds the <c>Tracon:Pricing:HostedTools</c> section.</summary>
    /// <remarks>
    /// The record is added even when <c>PerCall</c> is missing, for the reason
    /// <see cref="BindPricing"/> adds an empty model price: a misspelled key must reach
    /// <c>TraconOptionsValidator</c> and fail the start, not vanish.
    /// </remarks>
    private static void BindHostedToolPricing(IConfigurationSection section, TraconPricingOptions options, TraconOptions root)
    {
        if (!section.Exists())
        {
            return;
        }

        foreach (var providerSection in section.GetChildren())
        {
            var kinds = new Dictionary<string, HostedToolPriceOverride>(StringComparer.OrdinalIgnoreCase);

            foreach (var kindSection in providerSection.GetChildren())
            {
                var batch = ConfigurationValueReader.For(kindSection, options, root).Batch();
                var price = new HostedToolPriceOverride
                {
                    PerCall = batch.Decimal(kindSection, nameof(HostedToolPriceOverride.PerCall)),
                };

                kinds[kindSection.Key] = price;

                var providerName = providerSection.Key;
                var kindName = kindSection.Key;

                batch.Commit(() => ReferenceEquals(PriceOf(options.HostedTools, providerName, kindName), price));
            }

            if (kinds.Count > 0)
            {
                options.HostedTools[providerSection.Key] = kinds;
            }
        }
    }

    /// <summary>Binds the <c>Tracon:Pricing:Images</c> section.</summary>
    private static void BindImagePricing(IConfigurationSection section, TraconPricingOptions options, TraconOptions root)
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
                var batch = ConfigurationValueReader.For(modelSection, options, root).Batch();
                var price = new ImagePriceOverride
                {
                    PerImage = batch.Decimal(modelSection, nameof(ImagePriceOverride.PerImage)),
                    OutputCostPerMillionTokens = batch.Decimal(
                        modelSection,
                        nameof(ImagePriceOverride.OutputCostPerMillionTokens)),
                };

                // A multiplier that cannot be read is left out and named at startup: the old
                // stand-in of -1 reached the options validator as a negative price.
                var multipliers = modelSection.GetSection(nameof(ImagePriceOverride.SizeMultipliers));

                foreach (var multiplierSection in multipliers.GetChildren())
                {
                    if (batch.Decimal(multipliers, multiplierSection.Key) is { } multiplier)
                    {
                        price.SizeMultipliers[multiplierSection.Key] = multiplier;
                    }
                }

                // Keep an empty record. The validator must detect a misspelled
                // price key instead of silently making image generation unpriced.
                models[modelSection.Key] = price;

                var providerName = providerSection.Key;
                var modelName = modelSection.Key;

                batch.Commit(() => ReferenceEquals(PriceOf(options.Images, providerName, modelName), price));
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

        var read = ConfigurationValueReader.For(section, options);

        read.Boolean(
            nameof(TraconImageOptions.Enabled),
            static o => o.Enabled,
            static (o, v) => o.Enabled = v);

        if (section[nameof(TraconImageOptions.Provider)] is { Length: > 0 } provider)
        {
            options.Provider = provider;
        }

        if (section[nameof(TraconImageOptions.Model)] is { Length: > 0 } model)
        {
            options.Model = model;
        }

        read.Int32(
            nameof(TraconImageOptions.MaxImagesPerRequest),
            static o => o.MaxImagesPerRequest,
            static (o, v) => o.MaxImagesPerRequest = v);

        read.TimeSpan(
            nameof(TraconImageOptions.Timeout),
            static o => o.Timeout,
            static (o, v) => o.Timeout = v);
    }

    // The section was documented as read from Tracon:Knowledge and bound nowhere:
    // every value in configuration was ignored (F-416 found it while adding
    // EmbeddingProvider; the F-247 class).
    private static void BindKnowledge(IConfigurationSection section, TraconKnowledgeOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options);

        read.Int32(
            nameof(TraconKnowledgeOptions.Dimensions),
            static o => o.Dimensions,
            static (o, v) => o.Dimensions = v);

        read.Int32(
            nameof(TraconKnowledgeOptions.ChunkSize),
            static o => o.ChunkSize,
            static (o, v) => o.ChunkSize = v);

        read.Int32(
            nameof(TraconKnowledgeOptions.ChunkOverlap),
            static o => o.ChunkOverlap,
            static (o, v) => o.ChunkOverlap = v);

        read.Int32(
            nameof(TraconKnowledgeOptions.MaxResults),
            static o => o.MaxResults,
            static (o, v) => o.MaxResults = v);

        if (section[nameof(TraconKnowledgeOptions.EmbeddingProvider)] is { Length: > 0 } embeddingProvider)
        {
            options.EmbeddingProvider = embeddingProvider;
        }
    }

    /// <summary>Gets the price entry stored under a provider and a name, or <see langword="null"/>.</summary>
    private static T? PriceOf<T>(IDictionary<string, IDictionary<string, T>> prices, string provider, string name)
        where T : class
        => prices.TryGetValue(provider, out var models) && models.TryGetValue(name, out var price) ? price : null;

    /// <summary>
    /// Reads the utility model binding from configuration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ModelBinding.Provider"/> and <see cref="ModelBinding.Model"/>
    /// are required; when both are not set, no binding is built. A partially
    /// filled binding would silently accept a configuration that was
    /// mistakenly written incomplete.
    /// </para>
    /// <para>
    /// An empty number means "not set": the provider default applies. A number
    /// that does not parse is NOT skipped: the failed parse left the out
    /// value at zero and the non-empty key then wrote <c>Temperature = 0</c>, so a
    /// typo silently ran the summarizer at temperature zero. The binding is built
    /// without that number and the text is recorded, so
    /// <see cref="UnreadableConfigurationValidator{TOptions}"/> names it at startup.
    /// </para>
    /// </remarks>
    private static void BindUtilityModel(IConfigurationSection section, TraconOptions options)
    {
        options.UtilityModel = null;

        if (!section.Exists())
        {
            return;
        }

        var provider = section[nameof(ModelBinding.Provider)];
        var model = section[nameof(ModelBinding.Model)];

        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(model))
        {
            return;
        }

        var batch = ConfigurationValueReader.For(section, options).Batch();

        var binding = new ModelBinding
        {
            Provider = provider,
            Model = model,
            Temperature = batch.Single(section, nameof(ModelBinding.Temperature)),
            MaxOutputTokens = batch.Int32(section, nameof(ModelBinding.MaxOutputTokens)),
            TopP = batch.Single(section, nameof(ModelBinding.TopP)),
            ReasoningEffort = section[nameof(ModelBinding.ReasoningEffort)],
        };

        options.UtilityModel = binding;

        batch.Commit(() => ReferenceEquals(options.UtilityModel, binding));
    }

    private static void BindAgentGraph(IConfigurationSection section, TraconAgentGraphOptions options, TraconOptions root)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options, root);

        read.Int32(
            nameof(TraconAgentGraphOptions.MaxDepth),
            static o => o.MaxDepth,
            static (o, v) => o.MaxDepth = v);

        read.Int64(
            nameof(TraconAgentGraphOptions.MaxTotalTokens),
            static o => o.MaxTotalTokens,
            static (o, v) => o.MaxTotalTokens = v);

        read.Decimal(
            nameof(TraconAgentGraphOptions.MaxTotalCost),
            static o => o.MaxTotalCost,
            static (o, v) => o.MaxTotalCost = v);

        read.Int32(
            nameof(TraconAgentGraphOptions.MaxTotalRuns),
            static o => o.MaxTotalRuns,
            static (o, v) => o.MaxTotalRuns = v);

        read.TimeSpan(
            nameof(TraconAgentGraphOptions.MaxDuration),
            static o => o.MaxDuration,
            static (o, v) => o.MaxDuration = v);

        read.TimeSpan(
            nameof(TraconAgentGraphOptions.ChildDeadline),
            static o => o.ChildDeadline,
            static (o, v) => o.ChildDeadline = v);

        read.TimeSpan(
            nameof(TraconAgentGraphOptions.WaitTimeout),
            static o => o.WaitTimeout,
            static (o, v) => o.WaitTimeout = v);
    }

    private static void BindAttachments(IConfigurationSection section, TraconAttachmentOptions options, TraconOptions root)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options, root);

        read.Int64(
            nameof(TraconAttachmentOptions.MaxBytes),
            static o => o.MaxBytes,
            static (o, v) => o.MaxBytes = v);

        read.StringList(nameof(TraconAttachmentOptions.AllowedMediaTypes), options.AllowedMediaTypes);
    }
}
