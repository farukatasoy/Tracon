using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Extensions that add the Google Gemini provider to the AgentPrism chain.</summary>
public static class GoogleProviderExtensions
{
    /// <summary>Adds the Google provider by giving an API key.</summary>
    /// <param name="builder">AgentPrism chain.</param>
    /// <param name="apiKey">Gemini Developer API key.</param>
    /// <param name="configure">Additional settings changer.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="apiKey"/> is empty.</exception>
    /// <remarks>
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
    ///        .UseGoogle(builder.Configuration["Google:ApiKey"]!);
    /// </code>
    /// </example>
    /// </remarks>
    public static IAgentPrismBuilder UseGoogle(
        this IAgentPrismBuilder builder,
        string apiKey,
        Action<GoogleProviderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        return builder.UseGoogle(options =>
        {
            options.ApiKey = apiKey;
            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// Adds the Google provider, reading settings from the
    /// <c>AgentPrism:Providers:Google</c> section.
    /// </summary>
    /// <param name="builder">AgentPrism chain.</param>
    /// <param name="configurationSection">
    /// Section to read settings from. Typically
    /// <c>configuration.GetSection(GoogleProviderOptions.SectionName)</c>.
    /// </param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    public static IAgentPrismBuilder UseGoogle(
        this IAgentPrismBuilder builder,
        IConfiguration configurationSection)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return builder.UseGoogle(options => Bind(configurationSection, options));
    }

    /// <summary>Adds the Google provider by giving settings in code.</summary>
    /// <param name="builder">AgentPrism chain.</param>
    /// <param name="configure">Settings changer.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This call registers a single provider: <see cref="GoogleProviderNames.Google"/>.
    /// </para>
    /// <para>
    /// Registration uses <c>AddModelProvider(...)</c>; no existing service is
    /// <em>replaced</em> (decision K-025). When called more than once, settings are
    /// merged; the provider is registered only once.
    /// </para>
    /// </remarks>
    public static IAgentPrismBuilder UseGoogle(
        this IAgentPrismBuilder builder,
        Action<GoogleProviderOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var services = builder.Services;

        services.AddOptions<GoogleProviderOptions>().ValidateOnStart();
        services.Configure(configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<GoogleProviderOptions>,
            GoogleProviderOptionsValidator>());

        // A second call merges settings but does not register the provider again;
        // otherwise ModelProviderRegistry would raise a "name registered more than
        // once" error.
        var alreadyRegistered = services.Any(
            static descriptor => descriptor.ServiceType == typeof(GoogleChatClientFactory));

        // A single client, a single HTTP connection pool. The factory implements
        // IDisposable, so the client closes when the container shuts down.
        services.TryAddSingleton(static provider => new GoogleChatClientFactory(
            provider.GetRequiredService<IOptions<GoogleProviderOptions>>().Value,
            provider.GetService<ILoggerFactory>()));

        if (alreadyRegistered)
        {
            return builder;
        }

        builder.AddModelProvider(static provider =>
        {
            var options = provider.GetRequiredService<IOptions<GoogleProviderOptions>>().Value;

            return new GoogleModelProvider(
                GoogleProviderNames.Google,
                provider.GetRequiredService<GoogleChatClientFactory>(),
                GoogleModelCatalog.Build(options),
                provider.GetService<ILogger<GoogleModelProvider>>(),
                healthCheckOptions: options);
        });

        return builder;
    }

    /// <summary>Binds a configuration section into the settings object by hand.</summary>
    /// <remarks>
    /// <c>Bind()</c> relies on reflection and produces <c>IL2026</c> + <c>IL3050</c>.
    /// When a new setting is added, it must also be added to this method and to
    /// <see cref="GoogleProviderOptionsValidator"/>.
    /// Rationale: <c>docs/KARARLAR.md</c>, decision K-021.
    /// </remarks>
    internal static void Bind(IConfiguration section, GoogleProviderOptions options)
    {
        if (section[nameof(GoogleProviderOptions.ApiKey)] is { Length: > 0 } apiKey)
        {
            options.ApiKey = apiKey;
        }

        if (section[nameof(GoogleProviderOptions.DefaultModel)] is { Length: > 0 } defaultModel)
        {
            options.DefaultModel = defaultModel;
        }

        if (section[nameof(GoogleProviderOptions.Endpoint)] is { Length: > 0 } endpoint
            && Uri.TryCreate(endpoint, UriKind.RelativeOrAbsolute, out var endpointUri))
        {
            // Relative addresses are also assigned: the validator rejects them via
            // its IsAbsoluteUri check. Parsing only with UriKind.Absolute and
            // silently skipping a relative value would make that validator branch
            // unreachable.
            options.Endpoint = endpointUri;
        }

        if (section[nameof(GoogleProviderOptions.ApiVersion)] is { Length: > 0 } apiVersion)
        {
            options.ApiVersion = apiVersion;
        }

        if (TimeSpan.TryParse(
                section[nameof(GoogleProviderOptions.Timeout)],
                CultureInfo.InvariantCulture,
                out var timeout))
        {
            options.Timeout = timeout;
        }

        BindModels(section.GetSection(nameof(GoogleProviderOptions.Models)), options);
    }

    private static void BindModels(IConfiguration section, GoogleProviderOptions options)
    {
        foreach (var child in section.GetChildren())
        {
            // An empty/missing name is also added: the validator rejects it with a
            // loop over Models[i]. Skipping it here would make that validator branch
            // unreachable.
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
                // AgentPrism:Pricing cannot supply one either: the catalog price WINS
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
