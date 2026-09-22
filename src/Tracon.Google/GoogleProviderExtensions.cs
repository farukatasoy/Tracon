using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Extensions that add the Google Gemini provider to the Tracon chain.</summary>
public static class GoogleProviderExtensions
{
    /// <summary>Adds the Google provider by giving an API key.</summary>
    /// <param name="builder">Tracon chain.</param>
    /// <param name="apiKey">Gemini Developer API key.</param>
    /// <param name="configure">Additional settings changer.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="apiKey"/> is empty.</exception>
    /// <remarks>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseGoogle(builder.Configuration["Google:ApiKey"]!);
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder UseGoogle(
        this ITraconBuilder builder,
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
    /// <c>Tracon:Providers:Google</c> section.
    /// </summary>
    /// <param name="builder">Tracon chain.</param>
    /// <param name="configurationSection">
    /// Section to read settings from. Typically
    /// <c>configuration.GetSection(GoogleProviderOptions.SectionName)</c>.
    /// </param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    public static ITraconBuilder UseGoogle(
        this ITraconBuilder builder,
        IConfiguration configurationSection)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return builder.UseGoogle(options => Bind(configurationSection, options));
    }

    /// <summary>Adds the Google provider by giving settings in code.</summary>
    /// <param name="builder">Tracon chain.</param>
    /// <param name="configure">Settings changer.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This call registers a single provider: <see cref="GoogleProviderNames.Google"/>.
    /// </para>
    /// <para>
    /// Registration uses <c>AddModelProvider(...)</c>; no existing service is
    /// <em>replaced</em>. When called more than once, settings are
    /// merged; the provider is registered only once.
    /// </para>
    /// </remarks>
    public static ITraconBuilder UseGoogle(
        this ITraconBuilder builder,
        Action<GoogleProviderOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var services = builder.Services;

        ProviderRegistrationCore.AddValidatedOptions<GoogleProviderOptions, GoogleProviderOptionsValidator>(services, configure);

        var alreadyRegistered = ProviderRegistrationCore.IsRegistered<GoogleChatClientFactory>(services);

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
                healthCheckOptions: options,
                egressGuard: provider.GetService<EgressSocketGuard>());
        });

        return builder;
    }

    /// <summary>Binds a configuration section into the settings object by hand.</summary>
    /// <remarks>
    /// <c>Bind()</c> relies on reflection and produces <c>IL2026</c> + <c>IL3050</c>.
    /// When a new setting is added, it must also be added to this method and to
    /// <see cref="GoogleProviderOptionsValidator"/>.
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

        if (ProviderRegistrationCore.ReadEndpoint(section, nameof(GoogleProviderOptions.Endpoint)) is { } endpoint)
        {
            options.Endpoint = endpoint;
        }

        if (section[nameof(GoogleProviderOptions.ApiVersion)] is { Length: > 0 } apiVersion)
        {
            options.ApiVersion = apiVersion;
        }

        if (ProviderRegistrationCore.ReadTimeSpan(section, nameof(GoogleProviderOptions.Timeout)) is { } timeout)
        {
            options.Timeout = timeout;
        }

        ProviderRegistrationCore.BindModels(section.GetSection(nameof(GoogleProviderOptions.Models)), options.Models);
    }
}
