using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Extensions that add the Azure OpenAI provider to the AgentPrism chain.</summary>
public static class AzureOpenAIProviderExtensions
{
    /// <summary>Adds the Azure OpenAI provider by giving an endpoint and API key.</summary>
    /// <param name="builder">The AgentPrism chain.</param>
    /// <param name="endpoint">The Azure OpenAI resource's address.</param>
    /// <param name="apiKey">The resource's API key.</param>
    /// <param name="configure">An extra option modifier.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/>
    /// or
    /// <paramref name="endpoint"/>
    /// is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="apiKey"/> is empty.</exception>
    /// <remarks>
    /// To use a managed credential, use
    /// <see cref="UseAzureOpenAI(IAgentPrismBuilder, Action{AzureOpenAIProviderOptions})"/>
    /// instead of this overload, and give <see cref="AzureOpenAIProviderOptions.CredentialFactory"/>.
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
    ///        .UseAzureOpenAI(
    ///            new Uri(builder.Configuration["AzureOpenAI:Endpoint"]!),
    ///            builder.Configuration["AzureOpenAI:ApiKey"]!);
    /// </code>
    /// </example>
    /// </remarks>
    public static IAgentPrismBuilder UseAzureOpenAI(
        this IAgentPrismBuilder builder,
        Uri endpoint,
        string apiKey,
        Action<AzureOpenAIProviderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        return builder.UseAzureOpenAI(options =>
        {
            options.Endpoint = endpoint;
            options.ApiKey = apiKey;
            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// Adds the Azure OpenAI provider by reading options from the
    /// <c>AgentPrism:Providers:AzureOpenAI</c> section.
    /// </summary>
    /// <param name="builder">The AgentPrism chain.</param>
    /// <param name="configurationSection">
    /// The section to read options from. Typically
    /// <c>configuration.GetSection(AzureOpenAIProviderOptions.SectionName)</c>.
    /// </param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/>
    /// or
    /// <paramref name="configurationSection"/>
    /// is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <see cref="AzureOpenAIProviderOptions.CredentialFactory"/> cannot be read from
    /// configuration; call the <see cref="UseAzureOpenAI(IAgentPrismBuilder, Action{AzureOpenAIProviderOptions})"/>
    /// overload afterward to set it in code.
    /// </remarks>
    public static IAgentPrismBuilder UseAzureOpenAI(
        this IAgentPrismBuilder builder,
        IConfiguration configurationSection)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return builder.UseAzureOpenAI(options => Bind(configurationSection, options));
    }

    /// <summary>Adds the Azure OpenAI provider by giving options in code.</summary>
    /// <param name="builder">The AgentPrism chain.</param>
    /// <param name="configure">The option modifier.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This call registers exactly one provider: <see cref="AzureOpenAIProviderNames.AzureOpenAI"/>.
    /// </para>
    /// <para>
    /// Registration happens through <c>AddModelProvider(...)</c>; no existing
    /// service is <em>replaced</em>. When called more than
    /// once, the options are merged; the provider is registered only once.
    /// </para>
    /// </remarks>
    public static IAgentPrismBuilder UseAzureOpenAI(
        this IAgentPrismBuilder builder,
        Action<AzureOpenAIProviderOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var services = builder.Services;

        services.AddOptions<AzureOpenAIProviderOptions>().ValidateOnStart();
        services.Configure(configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<AzureOpenAIProviderOptions>,
            AzureOpenAIProviderOptionsValidator>());

        // A second call merges options but does not register the provider
        // again; otherwise ModelProviderRegistry would throw an "already
        // registered under this name" error and the cause would stay hidden
        // from the user.
        var alreadyRegistered = services.Any(
            static descriptor => descriptor.ServiceType == typeof(AzureOpenAIChatClientFactory));

        // One client, one HTTP connection pool.
        services.TryAddSingleton(static provider => new AzureOpenAIChatClientFactory(
            provider.GetRequiredService<IOptions<AzureOpenAIProviderOptions>>().Value,
            provider.GetService<ILoggerFactory>()));

        if (alreadyRegistered)
        {
            return builder;
        }

        builder.AddModelProvider(static provider =>
        {
            var options = provider.GetRequiredService<IOptions<AzureOpenAIProviderOptions>>().Value;

            return new AzureOpenAIModelProvider(
                AzureOpenAIProviderNames.AzureOpenAI,
                provider.GetRequiredService<AzureOpenAIChatClientFactory>(),
                AzureOpenAIModelCatalog.Build(options),
                provider.GetService<ILogger<AzureOpenAIModelProvider>>(),
                healthCheckOptions: options);
        });

        return builder;
    }

    /// <summary>Manually binds a configuration section onto the options object.</summary>
    /// <remarks>
    /// <para>
    /// <c>Bind()</c> relies on reflection and produces <c>IL2026</c> + <c>IL3050</c>.
    /// When a new option is added, it must also be added to this method and to
    /// <see cref="AzureOpenAIProviderOptionsValidator"/>.
    /// </para>
    /// <para>
    /// <see cref="AzureOpenAIProviderOptions.CredentialFactory"/> is deliberately
    /// not bound: a delegate cannot be read from configuration, and choosing a
    /// credential is a decision made in code.
    /// </para>
    /// </remarks>
    internal static void Bind(IConfiguration section, AzureOpenAIProviderOptions options)
    {
        if (section[nameof(AzureOpenAIProviderOptions.Endpoint)] is { Length: > 0 } endpoint
            && Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri))
        {
            options.Endpoint = endpointUri;
        }

        if (section[nameof(AzureOpenAIProviderOptions.ApiKey)] is { Length: > 0 } apiKey)
        {
            options.ApiKey = apiKey;
        }

        if (section[nameof(AzureOpenAIProviderOptions.DefaultDeployment)] is { Length: > 0 } defaultDeployment)
        {
            options.DefaultDeployment = defaultDeployment;
        }

        if (section[nameof(AzureOpenAIProviderOptions.Audience)] is { Length: > 0 } audience)
        {
            options.Audience = audience;
        }

        if (TimeSpan.TryParse(
                section[nameof(AzureOpenAIProviderOptions.Timeout)],
                CultureInfo.InvariantCulture,
                out var timeout))
        {
            options.Timeout = timeout;
        }

        BindModels(section.GetSection(nameof(AzureOpenAIProviderOptions.Models)), options);
    }

    private static void BindModels(IConfiguration section, AzureOpenAIProviderOptions options)
    {
        foreach (var child in section.GetChildren())
        {
            if (child[nameof(ModelDescriptor.Name)] is not { Length: > 0 } name)
            {
                continue;
            }

            options.Models.Add(new ModelDescriptor
            {
                Name = name,
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
