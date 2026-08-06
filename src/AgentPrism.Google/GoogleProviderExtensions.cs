using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>AgentPrism zincirine Google Gemini saglayicisini ekleyen uzantilar.</summary>
public static class GoogleProviderExtensions
{
    /// <summary>API anahtari vererek Google saglayicisini ekler.</summary>
    /// <param name="builder">AgentPrism zinciri.</param>
    /// <param name="apiKey">Gemini Developer API anahtari.</param>
    /// <param name="configure">Ek ayar degistirici.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> <see langword="null"/> ise.</exception>
    /// <exception cref="ArgumentException"><paramref name="apiKey"/> bos ise.</exception>
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
    /// Ayarlari <c>AgentPrism:Providers:Google</c> bolumunden okuyarak Google
    /// saglayicisini ekler.
    /// </summary>
    /// <param name="builder">AgentPrism zinciri.</param>
    /// <param name="configurationSection">
    /// Ayarlarin okunacagi bolum. Genellikle
    /// <c>configuration.GetSection(GoogleProviderOptions.SectionName)</c>.
    /// </param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException">Parametrelerden biri <see langword="null"/> ise.</exception>
    public static IAgentPrismBuilder UseGoogle(
        this IAgentPrismBuilder builder,
        IConfiguration configurationSection)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return builder.UseGoogle(options => Bind(configurationSection, options));
    }

    /// <summary>Ayarlari kodda vererek Google saglayicisini ekler.</summary>
    /// <param name="builder">AgentPrism zinciri.</param>
    /// <param name="configure">Ayar degistirici.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException">Parametrelerden biri <see langword="null"/> ise.</exception>
    /// <remarks>
    /// <para>
    /// Bu cagri tek bir saglayici kaydeder: <see cref="GoogleProviderNames.Google"/>.
    /// </para>
    /// <para>
    /// Kayit <c>AddModelProvider(...)</c> ile yapilir; mevcut hicbir servis
    /// <em>degistirilmez</em> (karar K-025). Birden cok kez cagrilirsa ayarlar
    /// birlestirilir; saglayici yalnizca bir kez kaydedilir.
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

        // Ikinci cagri ayarlari birlestirir ama saglayiciyi tekrar kaydetmez;
        // aksi halde ModelProviderRegistry "ayni ad birden cok kez kaydedilmis"
        // hatasi verirdi.
        var alreadyRegistered = services.Any(
            static descriptor => descriptor.ServiceType == typeof(GoogleChatClientFactory));

        // Tek istemci, tek HTTP baglanti havuzu. Fabrika IDisposable oldugu icin
        // kapsayici kapanirken istemci de kapanir.
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

    /// <summary>Yapilandirma bolumunu ayar nesnesine elle baglar.</summary>
    /// <remarks>
    /// <c>Bind()</c> yansimaya dayanir ve <c>IL2026</c> + <c>IL3050</c> uretir.
    /// Yeni bir ayar eklendiginde bu metoda ve
    /// <see cref="GoogleProviderOptionsValidator"/> icine de eklenmelidir.
    /// Gerekce: <c>docs/KARARLAR.md</c>, karar K-021.
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
            && Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri))
        {
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
