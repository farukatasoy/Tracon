using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>AgentPrism zincirine OpenAI saglayicisini ekleyen uzantilar.</summary>
public static class OpenAIProviderExtensions
{
    /// <summary>API anahtari vererek OpenAI saglayicisini ekler.</summary>
    /// <param name="builder">AgentPrism zinciri.</param>
    /// <param name="apiKey">OpenAI API anahtari.</param>
    /// <param name="configure">Ek ayar degistirici.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> <see langword="null"/> ise.</exception>
    /// <exception cref="ArgumentException"><paramref name="apiKey"/> bos ise.</exception>
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
    /// Ayarlari <c>AgentPrism:Providers:OpenAI</c> bolumunden okuyarak OpenAI
    /// saglayicisini ekler.
    /// </summary>
    /// <param name="builder">AgentPrism zinciri.</param>
    /// <param name="configurationSection">
    /// Ayarlarin okunacagi bolum. Genellikle
    /// <c>configuration.GetSection(OpenAIProviderOptions.SectionName)</c>.
    /// </param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException">Parametrelerden biri <see langword="null"/> ise.</exception>
    public static IAgentPrismBuilder UseOpenAI(
        this IAgentPrismBuilder builder,
        IConfiguration configurationSection)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return builder.UseOpenAI(options => Bind(configurationSection, options));
    }

    /// <summary>Ayarlari kodda vererek OpenAI saglayicisini ekler.</summary>
    /// <param name="builder">AgentPrism zinciri.</param>
    /// <param name="configure">Ayar degistirici.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException">Parametrelerden biri <see langword="null"/> ise.</exception>
    /// <remarks>
    /// <para>
    /// Bu cagri iki saglayici kaydeder: <see cref="OpenAIProviderNames.ChatCompletions"/>
    /// ve <see cref="OpenAIProviderNames.Responses"/>. Agent tanimi
    /// <see cref="ModelBinding.Provider"/> ile hangisini kullanacagini secer.
    /// </para>
    /// <para>
    /// Kayit <c>AddModelProvider(...)</c> ile yapilir; mevcut hicbir servis
    /// <em>degistirilmez</em>. <c>UsePostgreSql()</c> mevcut depolarin yerine gectigi
    /// icin <c>Replace</c> kullanir; bu cagri yeni bir saglayici <em>ekler</em>, bu
    /// yuzden ayni ihtiyac yoktur. Gerekce: <c>docs/KARARLAR.md</c>, karar K-025.
    /// </para>
    /// <para>
    /// Birden cok kez cagrilirsa ayarlar birlestirilir; saglayicilar yalnizca bir kez
    /// kaydedilir.
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

        // Ikinci cagri ayarlari birlestirir ama saglayicilari tekrar kaydetmez;
        // aksi halde ModelProviderRegistry "ayni ad birden cok kez kaydedilmis"
        // hatasi verirdi ve sebebi kullaniciya kapali kalirdi.
        var alreadyRegistered = services.Any(
            static descriptor => descriptor.ServiceType == typeof(OpenAIChatClientFactory));

        // Tek istemci, tek HTTP baglanti havuzu. Iki saglayici da bunu paylasir.
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
            provider.GetService<ILogger<OpenAIModelProvider>>());
    }

    /// <summary>
    /// Yapilandirma bolumunu ayar nesnesine elle baglar.
    /// </summary>
    /// <remarks>
    /// <c>Bind()</c> yansimaya dayanir ve <c>IL2026</c> + <c>IL3050</c> uretir.
    /// Yeni bir ayar eklendiginde bu metoda da eklenmelidir.
    /// Gerekce: <c>docs/KARARLAR.md</c>, karar K-021.
    /// </remarks>
    private static void Bind(IConfiguration section, OpenAIProviderOptions options)
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
            && Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri))
        {
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

        BindModels(section.GetSection(nameof(OpenAIProviderOptions.Models)), options);
    }

    private static void BindModels(IConfiguration section, OpenAIProviderOptions options)
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
