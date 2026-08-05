using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>AgentPrism zincirine Azure OpenAI saglayicisini ekleyen uzantilar.</summary>
public static class AzureOpenAIProviderExtensions
{
    /// <summary>Adres ve API anahtari vererek Azure OpenAI saglayicisini ekler.</summary>
    /// <param name="builder">AgentPrism zinciri.</param>
    /// <param name="endpoint">Azure OpenAI kaynaginin adresi.</param>
    /// <param name="apiKey">Kaynagin API anahtari.</param>
    /// <param name="configure">Ek ayar degistirici.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> veya <paramref name="endpoint"/> <see langword="null"/> ise.</exception>
    /// <exception cref="ArgumentException"><paramref name="apiKey"/> bos ise.</exception>
    /// <remarks>
    /// Yonetilen kimlik kullanmak icin bu asiri yukleme yerine
    /// <see cref="UseAzureOpenAI(IAgentPrismBuilder, Action{AzureOpenAIProviderOptions})"/>
    /// kullanin ve <see cref="AzureOpenAIProviderOptions.CredentialFactory"/> verin.
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
    /// Ayarlari <c>AgentPrism:Providers:AzureOpenAI</c> bolumunden okuyarak Azure
    /// OpenAI saglayicisini ekler.
    /// </summary>
    /// <param name="builder">AgentPrism zinciri.</param>
    /// <param name="configurationSection">
    /// Ayarlarin okunacagi bolum. Genellikle
    /// <c>configuration.GetSection(AzureOpenAIProviderOptions.SectionName)</c>.
    /// </param>
    /// <param name="configure">
    /// Yapilandirma baglandiktan sonra calisan ek degistirici. Yapilandirmadan
    /// okunamayan tek ayar olan
    /// <see cref="AzureOpenAIProviderOptions.CredentialFactory"/> burada verilir.
    /// </param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> veya <paramref name="configurationSection"/> <see langword="null"/> ise.</exception>
    public static IAgentPrismBuilder UseAzureOpenAI(
        this IAgentPrismBuilder builder,
        IConfiguration configurationSection,
        Action<AzureOpenAIProviderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return builder.UseAzureOpenAI(options =>
        {
            Bind(configurationSection, options);
            configure?.Invoke(options);
        });
    }

    /// <summary>Ayarlari kodda vererek Azure OpenAI saglayicisini ekler.</summary>
    /// <param name="builder">AgentPrism zinciri.</param>
    /// <param name="configure">Ayar degistirici.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException">Parametrelerden biri <see langword="null"/> ise.</exception>
    /// <remarks>
    /// <para>
    /// Bu cagri tek bir saglayici kaydeder: <see cref="AzureOpenAIProviderNames.AzureOpenAI"/>.
    /// </para>
    /// <para>
    /// Kayit <c>AddModelProvider(...)</c> ile yapilir; mevcut hicbir servis
    /// <em>degistirilmez</em> (karar K-025). Birden cok kez cagrilirsa ayarlar
    /// birlestirilir; saglayici yalnizca bir kez kaydedilir.
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

        // Ikinci cagri ayarlari birlestirir ama saglayiciyi tekrar kaydetmez;
        // aksi halde ModelProviderRegistry "ayni ad birden cok kez kaydedilmis"
        // hatasi verirdi ve sebebi kullaniciya kapali kalirdi.
        var alreadyRegistered = services.Any(
            static descriptor => descriptor.ServiceType == typeof(AzureOpenAIChatClientFactory));

        // Tek istemci, tek HTTP baglanti havuzu.
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

    /// <summary>Yapilandirma bolumunu ayar nesnesine elle baglar.</summary>
    /// <remarks>
    /// <para>
    /// <c>Bind()</c> yansimaya dayanir ve <c>IL2026</c> + <c>IL3050</c> uretir.
    /// Yeni bir ayar eklendiginde bu metoda ve
    /// <see cref="AzureOpenAIProviderOptionsValidator"/> icine de eklenmelidir.
    /// Gerekce: <c>docs/KARARLAR.md</c>, karar K-021.
    /// </para>
    /// <para>
    /// <see cref="AzureOpenAIProviderOptions.CredentialFactory"/> bilincli olarak
    /// baglanmaz: bir delegate yapilandirmadan okunamaz ve kimlik secimi kodda
    /// yapilan bir karardir.
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
