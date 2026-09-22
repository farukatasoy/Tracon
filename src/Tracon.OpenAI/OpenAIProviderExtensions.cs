using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Extensions that add the OpenAI provider to the Tracon chain.</summary>
public static class OpenAIProviderExtensions
{
    /// <summary>Adds the OpenAI provider with the given API key.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="apiKey">The OpenAI API key.</param>
    /// <param name="configure">An extra options callback.</param>
    /// <returns>The same chain, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="apiKey"/> is empty.</exception>
    /// <remarks>
    /// The key is passed in by the caller; Tracon never reads
    /// <c>IConfiguration</c> on its own, and the key is never written to the
    /// database.
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseOpenAI(builder.Configuration["OpenAI:ApiKey"]!);
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder UseOpenAI(
        this ITraconBuilder builder,
        string apiKey,
        Action<OpenAIProviderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        return builder.UseOpenAI(options =>
        {
            options.ApiKey = apiKey;
            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// Adds the OpenAI provider with options read from the
    /// <c>Tracon:Providers:OpenAI</c> section.
    /// </summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="configurationSection">
    /// The section the options are read from. Usually
    /// <c>configuration.GetSection(OpenAIProviderOptions.SectionName)</c>.
    /// </param>
    /// <returns>The same chain, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Any parameter is <see langword="null"/>.</exception>
    public static ITraconBuilder UseOpenAI(
        this ITraconBuilder builder,
        IConfiguration configurationSection)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return builder.UseOpenAI(options => Bind(configurationSection, options));
    }

    /// <summary>Adds the OpenAI provider with options set in code.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="configure">The options callback.</param>
    /// <returns>The same chain, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Any parameter is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This call registers two providers: <see cref="OpenAIProviderNames.ChatCompletions"/>
    /// and <see cref="OpenAIProviderNames.Responses"/>. The agent definition picks the one
    /// it uses through <see cref="ModelBinding.Provider"/>.
    /// </para>
    /// <para>
    /// Registration goes through <c>AddModelProvider(...)</c>; no existing service is
    /// <em>replaced</em>. <c>UsePostgreSql()</c> uses <c>Replace</c> because it takes the
    /// place of the existing stores; this call <em>adds</em> a new provider, so it has no
    /// such need.
    /// </para>
    /// <para>
    /// When it is called more than once, the options are merged; the providers are
    /// registered only once.
    /// </para>
    /// </remarks>
    public static ITraconBuilder UseOpenAI(
        this ITraconBuilder builder,
        Action<OpenAIProviderOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var services = builder.Services;

        ProviderRegistrationCore.AddValidatedOptions<OpenAIProviderOptions, OpenAIProviderOptionsValidator>(services, configure);

        var alreadyRegistered = ProviderRegistrationCore.IsRegistered<OpenAIChatClientFactory>(services);

        // One client, one HTTP connection pool. Both providers share it.
        services.TryAddSingleton(static provider => new OpenAIChatClientFactory(
            provider.GetRequiredService<IOptions<OpenAIProviderOptions>>().Value,
            provider.GetService<ILoggerFactory>()));

        if (alreadyRegistered)
        {
            return builder;
        }

        builder.AddModelProvider(static provider => CreateProvider(
            provider,
            OpenAIProviderNames.ChatCompletions,
            OpenAIApiSurface.ChatCompletions));

        builder.AddModelProvider(static provider => CreateProvider(
            provider,
            OpenAIProviderNames.Responses,
            OpenAIApiSurface.Responses));

        return builder;
    }

    private static OpenAIModelProvider CreateProvider(
        IServiceProvider provider,
        string name,
        OpenAIApiSurface apiSurface)
    {
        var options = provider.GetRequiredService<IOptions<OpenAIProviderOptions>>().Value;

        return new OpenAIModelProvider(
            name,
            apiSurface,
            provider.GetRequiredService<OpenAIChatClientFactory>(),
            OpenAIModelCatalog.Build(options),
            provider.GetService<ILogger<OpenAIModelProvider>>(),
            healthCheckOptions: options,
            egressGuard: provider.GetService<EgressSocketGuard>());
    }

    /// <summary>
    /// Binds the configuration section to the options object by hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Bind()</c> relies on reflection and produces <c>IL2026</c> + <c>IL3050</c>.
    /// When a new option is added, it must be added to this method too.
    /// </para>
    /// <para>
    /// <c>internal</c>: <c>OpenAICompatibleProviderExtensions</c> uses the same binding
    /// logic for named instances too; it is not copied.
    /// </para>
    /// </remarks>
    internal static void Bind(IConfiguration section, OpenAIProviderOptions options)
    {
        if (section[nameof(OpenAIProviderOptions.ApiKey)] is { Length: > 0 } apiKey)
        {
            options.ApiKey = apiKey;
        }

        if (section[nameof(OpenAIProviderOptions.DefaultModel)] is { Length: > 0 } defaultModel)
        {
            options.DefaultModel = defaultModel;
        }

        if (ProviderRegistrationCore.ReadEndpoint(section, nameof(OpenAIProviderOptions.Endpoint)) is { } endpoint)
        {
            options.Endpoint = endpoint;
        }

        if (section[nameof(OpenAIProviderOptions.Organization)] is { Length: > 0 } organization)
        {
            options.Organization = organization;
        }

        if (ProviderRegistrationCore.ReadTimeSpan(section, nameof(OpenAIProviderOptions.Timeout)) is { } timeout)
        {
            options.Timeout = timeout;
        }

        if (bool.TryParse(section[nameof(OpenAIProviderOptions.EnableResponsesSurface)], out var enableResponses))
        {
            options.EnableResponsesSurface = enableResponses;
        }

        ProviderRegistrationCore.BindModels(section.GetSection(nameof(OpenAIProviderOptions.Models)), options.Models);
    }
}
