using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Extensions that add named providers with an OpenAI compatible chat API to the
/// Tracon chain (OpenRouter, Groq, vLLM, Ollama, LM Studio, ...).
/// </summary>
/// <remarks>
/// <para>
/// The difference from <c>UseOpenAI()</c>: this call takes a <strong>name</strong> and
/// registers one or two providers (see
/// <see cref="OpenAIProviderOptions.EnableResponsesSurface"/>) under that name. It uses
/// the same <c>OpenAIProviderOptions</c> shape; the base address
/// (<see cref="OpenAIProviderOptions.Endpoint"/>) points at the compatible server.
/// </para>
/// <para>
/// Only the Chat Completions surface is registered; the Responses surface is
/// <strong>off</strong> by default (most compatible servers do not implement
/// <c>/v1/responses</c>).
/// </para>
/// </remarks>
public static class OpenAICompatibleProviderExtensions
{
    private const int MaxNameLength = 32;

    /// <summary>Adds a named OpenAI compatible provider whose options are set in code.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="name">
    /// The provider name. It is limited to <c>[a-z0-9][a-z0-9-]{0,31}</c>.
    /// <see cref="OpenAIProviderNames.ChatCompletions"/> and
    /// <see cref="OpenAIProviderNames.Responses"/> are reserved.
    /// </param>
    /// <param name="configure">The options callback. It must set at least <see cref="OpenAIProviderOptions.Endpoint"/>.</param>
    /// <returns>The same chain, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/>
    /// or
    /// <paramref name="configure"/>
    /// is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty, reserved, or does not match the pattern.</exception>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseOpenAICompatible("openrouter", o =>
    ///        {
    ///            o.Endpoint = new Uri("https://openrouter.ai/api/v1");
    ///            o.ApiKey   = configuration["OpenRouter:ApiKey"];
    ///        })
    ///        .UseOpenAICompatible("ollama", o =>
    ///        {
    ///            o.Endpoint = new Uri("http://localhost:11434/v1");
    ///            // No ApiKey — the local server does not ask for one
    ///        });
    /// </code>
    /// </example>
    public static ITraconBuilder UseOpenAICompatible(
        this ITraconBuilder builder,
        string name,
        Action<OpenAIProviderOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ValidateName(name);
        ArgumentNullException.ThrowIfNull(configure);

        var services = builder.Services;

        services.AddOptions<OpenAIProviderOptions>(name).ValidateOnStart();
        services.Configure(name, configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<OpenAIProviderOptions>,
            OpenAIProviderOptionsValidator>());
        services.TryAddSingleton<OpenAINamedChatClientFactoryCache>();

        // A second call merges the options but does not register the providers again;
        // the same reason as UseOpenAI() (the "AddModelProvider is enough" note of K-025).
        if (services.Any(descriptor =>
                descriptor.ServiceType == typeof(NamedProviderMarker)
                && descriptor.ImplementationInstance is NamedProviderMarker marker
                && string.Equals(marker.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return builder;
        }

        services.AddSingleton(new NamedProviderMarker(name));

        // We must know whether the Responses surface is on at REGISTRATION time: which
        // providers IModelProviderRegistry contains is decided while DI is built, not
        // while the options are resolved. Because configure is a side effect free Action
        // (the standard Options pattern), running it once more here against a throwaway
        // instance is safe.
        var probe = new OpenAIProviderOptions();
        configure(probe);

        builder.AddModelProvider(provider => CreateProvider(provider, name, name, OpenAIApiSurface.ChatCompletions));

        if (probe.EnableResponsesSurface)
        {
            builder.AddModelProvider(provider => CreateProvider(
                provider, name, $"{name}-responses", OpenAIApiSurface.Responses));
        }

        return builder;
    }

    /// <summary>
    /// Adds a named OpenAI compatible provider whose options are read from the
    /// <c>Tracon:Providers:OpenAICompatible:{name}</c> section.
    /// </summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="name">
    /// The provider name. See <see cref="UseOpenAICompatible(ITraconBuilder,
    /// string, Action{OpenAIProviderOptions})"/>.
    /// </param>
    /// <param name="configurationSection">
    /// The section the options are read from. Usually
    /// <c>configuration.GetSection($"{OpenAICompatibleProviderOptions.SectionName}:{name}")</c>.
    /// </param>
    /// <returns>The same chain, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Any parameter is <see langword="null"/>.</exception>
    public static ITraconBuilder UseOpenAICompatible(
        this ITraconBuilder builder,
        string name,
        IConfiguration configurationSection)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ValidateName(name);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return builder.UseOpenAICompatible(name, options => OpenAIProviderExtensions.Bind(configurationSection, options));
    }

    private static OpenAIModelProvider CreateProvider(
        IServiceProvider provider,
        string optionsName,
        string providerName,
        OpenAIApiSurface apiSurface)
    {
        var options = provider.GetRequiredService<IOptionsMonitor<OpenAIProviderOptions>>().Get(optionsName);
        var factory = provider.GetRequiredService<OpenAINamedChatClientFactoryCache>().Get(optionsName);

        return new OpenAIModelProvider(
            providerName,
            apiSurface,
            factory,
            OpenAIModelCatalog.Build(options),
            provider.GetService<ILogger<OpenAIModelProvider>>(),
            healthCheckOptions: options,
            // UseOpenAICompatible() does not bind to a fixed configuration section — the
            // key can come from any source the caller picks in code (for example
            // configuration["OpenRouter:ApiKey"]). Because there is no fixed path to
            // report, the diagnostics report shows NO ConfigurationDiagnostic at all for
            // this provider.
            configurationSectionKey: null);
    }

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (string.Equals(name, OpenAIProviderNames.ChatCompletions, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, OpenAIProviderNames.Responses, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The provider name '{name}' is reserved. '{OpenAIProviderNames.ChatCompletions}' and " +
                $"'{OpenAIProviderNames.Responses}' are used by UseOpenAI() only; the " +
                "ModelBinding.Provider values of agent definitions rely on those names.",
                nameof(name));
        }

        if (!IsValidNamePattern(name))
        {
            throw new ArgumentException(
                $"'{name}' is not a valid provider name. The name must contain lower case " +
                "letters, digits and hyphens, must start with a lower case letter or a digit, " +
                "and must be at most 32 characters long (for example 'openrouter', 'local-vllm').",
                nameof(name));
        }
    }

    /// <summary>
    /// Validates the same rule as the name pattern <c>^[a-z0-9][a-z0-9-]{0,31}$</c>.
    /// </summary>
    /// <remarks>
    /// It checks the characters by hand instead of using a regular expression: MA0009
    /// (regex DoS analysis) also flags source generated regular expressions, and a
    /// pattern this simple does not justify a suppression.
    /// </remarks>
    private static bool IsValidNamePattern(string name)
    {
        if (name.Length > MaxNameLength || !IsLowerAlphaNumeric(name[0]))
        {
            return false;
        }

        foreach (var character in name)
        {
            if (!IsLowerAlphaNumeric(character) && character != '-')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLowerAlphaNumeric(char character)
        => character is (>= 'a' and <= 'z') or (>= '0' and <= '9');

    private sealed record NamedProviderMarker(string Name);
}
