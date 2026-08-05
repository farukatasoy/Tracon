using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>AgentPrism zincirine Anthropic (Claude) saglayicisini ekleyen uzantilar.</summary>
public static class AnthropicProviderExtensions
{
    /// <summary>API anahtari vererek Anthropic saglayicisini ekler.</summary>
    /// <param name="builder">AgentPrism zinciri.</param>
    /// <param name="apiKey">Anthropic API anahtari.</param>
    /// <param name="configure">Ek ayar degistirici.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> <see langword="null"/> ise.</exception>
    /// <exception cref="ArgumentException"><paramref name="apiKey"/> bos ise.</exception>
    public static IAgentPrismBuilder UseAnthropic(
        this IAgentPrismBuilder builder,
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
    /// Ayarlari <c>AgentPrism:Providers:Anthropic</c> bolumunden okuyarak Anthropic
    /// saglayicisini ekler.
    /// </summary>
    /// <param name="builder">AgentPrism zinciri.</param>
    /// <param name="configurationSection">
    /// Ayarlarin okunacagi bolum. Genellikle
    /// <c>configuration.GetSection(AnthropicProviderOptions.SectionName)</c>.
    /// </param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException">Parametrelerden biri <see langword="null"/> ise.</exception>
    public static IAgentPrismBuilder UseAnthropic(
        this IAgentPrismBuilder builder,
        IConfiguration configurationSection)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return builder.UseAnthropic(options => Bind(configurationSection, options));
    }

    /// <summary>Ayarlari kodda vererek Anthropic saglayicisini ekler.</summary>
    /// <param name="builder">AgentPrism zinciri.</param>
    /// <param name="configure">Ayar degistirici.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException">Parametrelerden biri <see langword="null"/> ise.</exception>
    /// <remarks>
    /// <para>
    /// Bu cagri tek bir saglayici kaydeder: <see cref="AnthropicProviderNames.Anthropic"/>.
    /// </para>
    /// <para>
    /// Kayit <c>AddModelProvider(...)</c> ile yapilir; mevcut hicbir servis
    /// <em>degistirilmez</em> (karar K-025). Birden cok kez cagrilirsa ayarlar
    /// birlestirilir; saglayici yalnizca bir kez kaydedilir.
    /// </para>
    /// </remarks>
    public static IAgentPrismBuilder UseAnthropic(
        this IAgentPrismBuilder builder,
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

        // Ikinci cagri ayarlari birlestirir ama saglayiciyi tekrar kaydetmez;
        // aksi halde ModelProviderRegistry "ayni ad birden cok kez kaydedilmis"
        // hatasi verirdi ve sebebi kullaniciya kapali kalirdi.
        var alreadyRegistered = services.Any(
            static descriptor => descriptor.ServiceType == typeof(AnthropicChatClientFactory));

        // Tek istemci, tek HTTP baglanti havuzu.
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
                healthCheckOptions: options);
        });

        return builder;
    }

    /// <summary>Yapilandirma bolumunu ayar nesnesine elle baglar.</summary>
    /// <remarks>
    /// <c>Bind()</c> yansimaya dayanir ve <c>IL2026</c> + <c>IL3050</c> uretir.
    /// Yeni bir ayar eklendiginde bu metoda ve
    /// <see cref="AnthropicProviderOptionsValidator"/> icine de eklenmelidir.
    /// Gerekce: <c>docs/KARARLAR.md</c>, karar K-021.
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
            && Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri))
        {
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
