using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Extensions that add the OpenAI provider to the AgentPrism chain.</summary>
public static class OpenAIProviderExtensions
{
    /// <summary>Adds the OpenAI provider with the given API key.</summary>
    /// <param name="builder">The AgentPrism chain.</param>
    /// <param name="apiKey">The OpenAI API key.</param>
    /// <param name="configure">An extra options callback.</param>
    /// <returns>The same chain, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="apiKey"/> is empty.</exception>
    public static IAgentPrismBuilder UseOpenAI(
        this IAgentPrismBuilder builder,
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
    /// <c>AgentPrism:Providers:OpenAI</c> section.
    /// </summary>
    /// <param name="builder">The AgentPrism chain.</param>
    /// <param name="configurationSection">
    /// The section the options are read from. Usually
    /// <c>configuration.GetSection(OpenAIProviderOptions.SectionName)</c>.
    /// </param>
    /// <returns>The same chain, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Any parameter is <see langword="null"/>.</exception>
    public static IAgentPrismBuilder UseOpenAI(
        this IAgentPrismBuilder builder,
        IConfiguration configurationSection)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return builder.UseOpenAI(options => Bind(configurationSection, options));
    }

    /// <summary>Adds the OpenAI provider with options set in code.</summary>
    /// <param name="builder">The AgentPrism chain.</param>
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
    /// such need. Reason: <c>docs/KARARLAR.md</c>, decision K-025.
    /// </para>
    /// <para>
    /// When it is called more than once, the options are merged; the providers are
    /// registered only once.
    /// </para>
    /// </remarks>
    public static IAgentPrismBuilder UseOpenAI(
        this IAgentPrismBuilder builder,
        Action<OpenAIProviderOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var services = builder.Services;

        services.AddOptions<OpenAIProviderOptions>().ValidateOnStart();
        services.Configure(configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<OpenAIProviderOptions>,
            OpenAIProviderOptionsValidator>());

        // A second call merges the options but does not register the providers again;
        // otherwise ModelProviderRegistry would fail with "the same name is registered
        // more than once" and the reason would stay hidden from the user.
        var alreadyRegistered = services.Any(
            static descriptor => descriptor.ServiceType == typeof(OpenAIChatClientFactory));

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
            healthCheckOptions: options);
    }

    /// <summary>
    /// Binds the configuration section to the options object by hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Bind()</c> relies on reflection and produces <c>IL2026</c> + <c>IL3050</c>.
    /// When a new option is added, it must be added to this method too.
    /// Reason: <c>docs/KARARLAR.md</c>, decision K-021.
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

        if (section[nameof(OpenAIProviderOptions.Endpoint)] is { Length: > 0 } endpoint
            && Uri.TryCreate(endpoint, UriKind.RelativeOrAbsolute, out var endpointUri))
        {
            // Relative addresses are assigned too: the validator rejects them with its
            // IsAbsoluteUri check. Parsing with UriKind.Absolute only, and silently
            // skipping a relative address, would leave that validator branch unreachable.
            options.Endpoint = endpointUri;
        }

        if (section[nameof(OpenAIProviderOptions.Organization)] is { Length: > 0 } organization)
        {
            options.Organization = organization;
        }

        if (TimeSpan.TryParse(
                section[nameof(OpenAIProviderOptions.Timeout)],
                CultureInfo.InvariantCulture,
                out var timeout))
        {
            options.Timeout = timeout;
        }

        if (bool.TryParse(section[nameof(OpenAIProviderOptions.EnableResponsesSurface)], out var enableResponses))
        {
            options.EnableResponsesSurface = enableResponses;
        }

        BindModels(section.GetSection(nameof(OpenAIProviderOptions.Models)), options);
    }

    private static void BindModels(IConfiguration section, OpenAIProviderOptions options)
    {
        foreach (var child in section.GetChildren())
        {
            // An empty or missing name is added too: the validator rejects it in its
            // Models[i] loop. Skipping it here would leave that validator branch unreachable.
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
