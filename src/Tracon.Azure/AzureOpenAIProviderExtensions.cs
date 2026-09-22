using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Extensions that add the Azure OpenAI provider to the Tracon chain.</summary>
public static class AzureOpenAIProviderExtensions
{
    /// <summary>Adds the Azure OpenAI provider by giving an endpoint and API key.</summary>
    /// <param name="builder">The Tracon chain.</param>
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
    /// <see cref="UseAzureOpenAI(ITraconBuilder, Action{AzureOpenAIProviderOptions})"/>
    /// instead of this overload, and give <see cref="AzureOpenAIProviderOptions.CredentialFactory"/>.
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseAzureOpenAI(
    ///            new Uri(builder.Configuration["AzureOpenAI:Endpoint"]!),
    ///            builder.Configuration["AzureOpenAI:ApiKey"]!);
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder UseAzureOpenAI(
        this ITraconBuilder builder,
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
    /// <c>Tracon:Providers:AzureOpenAI</c> section.
    /// </summary>
    /// <param name="builder">The Tracon chain.</param>
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
    /// configuration; call the <see cref="UseAzureOpenAI(ITraconBuilder, Action{AzureOpenAIProviderOptions})"/>
    /// overload afterward to set it in code.
    /// </remarks>
    public static ITraconBuilder UseAzureOpenAI(
        this ITraconBuilder builder,
        IConfiguration configurationSection)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return builder.UseAzureOpenAI(options => Bind(configurationSection, options));
    }

    /// <summary>Adds the Azure OpenAI provider by giving options in code.</summary>
    /// <param name="builder">The Tracon chain.</param>
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
    public static ITraconBuilder UseAzureOpenAI(
        this ITraconBuilder builder,
        Action<AzureOpenAIProviderOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var services = builder.Services;

        ProviderRegistrationCore.AddValidatedOptions<AzureOpenAIProviderOptions, AzureOpenAIProviderOptionsValidator>(services, configure);

        var alreadyRegistered = ProviderRegistrationCore.IsRegistered<AzureOpenAIChatClientFactory>(services);

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
                healthCheckOptions: options,
                egressGuard: provider.GetService<EgressSocketGuard>());
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
        if (ProviderRegistrationCore.ReadEndpoint(section, nameof(AzureOpenAIProviderOptions.Endpoint)) is { } endpoint)
        {
            options.Endpoint = endpoint;
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

        if (ProviderRegistrationCore.ReadTimeSpan(section, nameof(AzureOpenAIProviderOptions.Timeout)) is { } timeout)
        {
            options.Timeout = timeout;
        }

        ProviderRegistrationCore.BindModels(section.GetSection(nameof(AzureOpenAIProviderOptions.Models)), options.Models);
    }
}
