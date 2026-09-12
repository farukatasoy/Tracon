using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Extensions that add OpenAI's live voice provider to the Tracon chain.</summary>
/// <remarks>
/// <para>
/// This is a <strong>separate call</strong>, not a flag on <c>UseOpenAI()</c>.
/// It opens an outbound connection that is billed by the second, and an application
/// must opt into that explicitly rather than inherit it from an unrelated call. The
/// separation also makes the <c>501</c> the endpoints return diagnosable: it says
/// which call is missing.
/// </para>
/// <para>
/// The API key is reused from <see cref="OpenAIProviderOptions"/>, so
/// <c>UseOpenAI(...)</c> must be called first. The validator says so by name if it
/// is not.
/// </para>
/// </remarks>
public static class OpenAILiveProviderExtensions
{
    /// <summary>Adds the OpenAI live voice provider, reading settings from configuration.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="configurationSection">The <c>Tracon:Providers:OpenAI:Live</c> section.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseOpenAI(builder.Configuration.GetSection(OpenAIProviderOptions.SectionName))
    ///        .UseOpenAILive(builder.Configuration.GetSection(OpenAILiveOptions.SectionName))
    ///        .UseLiveVoice();
    /// </code>
    /// </example>
    public static ITraconBuilder UseOpenAILive(
        this ITraconBuilder builder,
        IConfiguration configurationSection)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return UseOpenAILiveCore(builder, options => Bind(configurationSection, options));
    }

    /// <summary>Adds the OpenAI live voice provider with default settings.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static ITraconBuilder UseOpenAILive(this ITraconBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return UseOpenAILiveCore(builder, configure: null);
    }

    /// <summary>Adds the OpenAI live voice provider with settings given in code.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="configure">The settings.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    public static ITraconBuilder UseOpenAILive(
        this ITraconBuilder builder,
        Action<OpenAILiveOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        return UseOpenAILiveCore(builder, configure);
    }

    private static ITraconBuilder UseOpenAILiveCore(
        ITraconBuilder builder,
        Action<OpenAILiveOptions>? configure)
    {
        var services = builder.Services;

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<OpenAILiveOptions>, OpenAILiveOptionsValidator>());

        // TryAdd: a consumer that registered its own live provider keeps theirs.
        services.TryAddSingleton<ILiveVoiceProvider>(static provider => new OpenAILiveProvider(
            provider.GetRequiredService<IOptionsMonitor<OpenAILiveOptions>>(),
            provider.GetRequiredService<IOptionsMonitor<OpenAIProviderOptions>>(),
            provider.GetRequiredService<EgressSocketGuard>(),
            provider.GetRequiredService<ILoggerFactory>()));

        return builder;
    }

    /// <summary>Binds the live section by hand.</summary>
    /// <remarks>
    /// Hand-written because <c>Bind</c> uses reflection and the package must stay
    /// AOT-clean. A key forgotten here does not fail — it silently falls back to the
    /// default — so every property added to the options must be added here too.
    /// </remarks>
    private static void Bind(IConfiguration section, OpenAILiveOptions options)
    {
        if (section["Model"] is { Length: > 0 } model)
        {
            options.Model = model;
        }

        if (section["Voice"] is { Length: > 0 } voice)
        {
            options.Voice = voice;
        }

        if (section["Endpoint"] is { Length: > 0 } endpoint
            && Uri.TryCreate(endpoint, UriKind.Absolute, out var parsed))
        {
            options.Endpoint = parsed;
        }

        if (section["BackendModel"] is { Length: > 0 } backendModel)
        {
            options.BackendModel = backendModel;
        }

        if (int.TryParse(
                section["MaxAppendCharacters"],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxAppendCharacters))
        {
            options.MaxAppendCharacters = maxAppendCharacters;
        }

        if (TimeSpan.TryParse(section["Timeout"], CultureInfo.InvariantCulture, out var timeout))
        {
            options.Timeout = timeout;
        }
    }
}
