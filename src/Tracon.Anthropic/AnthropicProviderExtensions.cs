using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Extensions that add the Anthropic (Claude) provider to the Tracon chain.</summary>
public static class AnthropicProviderExtensions
{
    /// <summary>Adds the Anthropic provider using an API key.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="apiKey">Anthropic API key.</param>
    /// <param name="configure">Additional settings modifier.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="apiKey"/> is empty.</exception>
    /// <remarks>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseAnthropic(builder.Configuration["Anthropic:ApiKey"]!);
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder UseAnthropic(
        this ITraconBuilder builder,
        string apiKey,
        Action<AnthropicProviderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        return builder.UseAnthropic(options =>
        {
            options.ApiKey = apiKey;
            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// Adds the Anthropic provider, reading settings from the
    /// <c>Tracon:Providers:Anthropic</c> section.
    /// </summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="configurationSection">
    /// The section settings are read from. Typically
    /// <c>configuration.GetSection(AnthropicProviderOptions.SectionName)</c>.
    /// </param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    public static ITraconBuilder UseAnthropic(
        this ITraconBuilder builder,
        IConfiguration configurationSection)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return builder.UseAnthropic(options => Bind(configurationSection, options));
    }

    /// <summary>Adds the Anthropic provider using settings given in code.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="configure">Settings modifier.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This call registers a single provider: <see cref="AnthropicProviderNames.Anthropic"/>.
    /// </para>
    /// <para>
    /// Registration uses <c>AddModelProvider(...)</c>; no existing service is
    /// <em>replaced</em>. Calling this more than once merges the
    /// settings; the provider is registered only once.
    /// </para>
    /// </remarks>
    public static ITraconBuilder UseAnthropic(
        this ITraconBuilder builder,
        Action<AnthropicProviderOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var services = builder.Services;

        services.AddOptions<AnthropicProviderOptions>().ValidateOnStart();
        services.Configure(configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<AnthropicProviderOptions>,
            AnthropicProviderOptionsValidator>());

        // A second call merges settings but does not register the provider again;
        // otherwise ModelProviderRegistry would raise a "same name registered
        // more than once" error whose cause would stay hidden from the caller.
        var alreadyRegistered = services.Any(
            static descriptor => descriptor.ServiceType == typeof(AnthropicChatClientFactory));

        // One client, one HTTP connection pool.
        services.TryAddSingleton(static provider => new AnthropicChatClientFactory(
            provider.GetRequiredService<IOptions<AnthropicProviderOptions>>().Value,
            provider.GetService<ILoggerFactory>()));

        if (alreadyRegistered)
        {
            return builder;
        }

        builder.AddModelProvider(static provider =>
        {
            var options = provider.GetRequiredService<IOptions<AnthropicProviderOptions>>().Value;

            return new AnthropicModelProvider(
                AnthropicProviderNames.Anthropic,
                provider.GetRequiredService<AnthropicChatClientFactory>(),
                AnthropicModelCatalog.Build(options),
                provider.GetService<ILogger<AnthropicModelProvider>>(),
                healthCheckOptions: options,
                egressGuard: provider.GetService<EgressSocketGuard>());
        });

        return builder;
    }

    /// <summary>Binds a configuration section to the settings object by hand.</summary>
    /// <remarks>
    /// <c>Bind()</c> relies on reflection and produces <c>IL2026</c> + <c>IL3050</c>.
    /// When a new setting is added, it must also be added here and inside
    /// <see cref="AnthropicProviderOptionsValidator"/>.
    /// </remarks>
    internal static void Bind(IConfiguration section, AnthropicProviderOptions options)
    {
        if (section[nameof(AnthropicProviderOptions.ApiKey)] is { Length: > 0 } apiKey)
        {
            options.ApiKey = apiKey;
        }

        if (section[nameof(AnthropicProviderOptions.DefaultModel)] is { Length: > 0 } defaultModel)
        {
            options.DefaultModel = defaultModel;
        }

        if (section[nameof(AnthropicProviderOptions.Endpoint)] is { Length: > 0 } endpoint
            && Uri.TryCreate(endpoint, UriKind.RelativeOrAbsolute, out var endpointUri))
        {
            // Relative addresses are assigned too: the validator rejects them with
            // its IsAbsoluteUri check. Parsing only with UriKind.Absolute and
            // silently dropping a relative value would leave that validator branch
            // unreachable.
            options.Endpoint = endpointUri;
        }

        if (ReadInt32(section, nameof(AnthropicProviderOptions.DefaultMaxOutputTokens)) is { } maxOutputTokens)
        {
            options.DefaultMaxOutputTokens = maxOutputTokens;
        }

        if (ReadInt32(section, nameof(AnthropicProviderOptions.MaxRetries)) is { } maxRetries)
        {
            options.MaxRetries = maxRetries;
        }

        if (TimeSpan.TryParse(
                section[nameof(AnthropicProviderOptions.Timeout)],
                CultureInfo.InvariantCulture,
                out var timeout))
        {
            options.Timeout = timeout;
        }

        BindModels(section.GetSection(nameof(AnthropicProviderOptions.Models)), options);
    }

    private static void BindModels(IConfiguration section, AnthropicProviderOptions options)
    {
        foreach (var child in section.GetChildren())
        {
            // An empty/missing name is added too: the validator rejects it through
            // its Models[i] loop. Skipping it here would leave that validator
            // branch unreachable.
            options.Models.Add(new ModelDescriptor
            {
                Name = child[nameof(ModelDescriptor.Name)] ?? string.Empty,
                DisplayName = child[nameof(ModelDescriptor.DisplayName)],
                ContextWindowTokens = ReadInt32(child, nameof(ModelDescriptor.ContextWindowTokens)),
                MaxOutputTokens = ReadInt32(child, nameof(ModelDescriptor.MaxOutputTokens)),
                SupportsStreaming = ReadBoolean(child, nameof(ModelDescriptor.SupportsStreaming)) ?? true,
                SupportsTools = ReadBoolean(child, nameof(ModelDescriptor.SupportsTools)) ?? true,
                SupportsReasoning = ReadBoolean(child, nameof(ModelDescriptor.SupportsReasoning)) ?? false,
                SupportsStructuredOutput = ReadBoolean(child, nameof(ModelDescriptor.SupportsStructuredOutput)) ?? false,
                InputCostPerMillionTokens = ReadDecimal(child, nameof(ModelDescriptor.InputCostPerMillionTokens)),
                OutputCostPerMillionTokens = ReadDecimal(child, nameof(ModelDescriptor.OutputCostPerMillionTokens)),

                // 🚨 Without this line the catalog can never carry a cache rate, and
                // Tracon:Pricing cannot supply one either: the catalog price WINS
                // over the configured one, so a model priced here would silently fall
                // back to charging every cached token at the full input rate.
                CachedInputCostPerMillionTokens = ReadDecimal(child, nameof(ModelDescriptor.CachedInputCostPerMillionTokens)),
            });
        }
    }

    private static int? ReadInt32(IConfiguration section, string key)
        => int.TryParse(section[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static decimal? ReadDecimal(IConfiguration section, string key)
        => decimal.TryParse(section[key], NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static bool? ReadBoolean(IConfiguration section, string key)
        => bool.TryParse(section[key], out var value) ? value : null;
}
