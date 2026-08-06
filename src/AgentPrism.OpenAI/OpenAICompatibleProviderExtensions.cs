using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// AgentPrism zincirine adlandirilmis, OpenAI uyumlu sohbet API'leri sunan
/// saglayicilar ekleyen uzantilar (OpenRouter, Groq, vLLM, Ollama, LM Studio, ...).
/// </summary>
/// <remarks>
/// <para>
/// <c>UseOpenAI()</c>'den farki: bu cagri <strong>ad</strong> alir ve o adla bir veya
/// iki (bkz. <see cref="OpenAIProviderOptions.EnableResponsesSurface"/>) saglayici
/// kaydeder. Ayni <c>OpenAIProviderOptions</c> sekli kullanilir; taban adres
/// (<see cref="OpenAIProviderOptions.Endpoint"/>) uyumlu sunucuya isaret eder.
/// </para>
/// <para>
/// Sadece Chat Completions yuzeyi kaydedilir; Responses yuzeyi varsayilan olarak
/// <strong>kapalidir</strong> (cogu uyumlu sunucu <c>/v1/responses</c> uygulamaz).
/// </para>
/// </remarks>
public static class OpenAICompatibleProviderExtensions
{
    private const int MaxNameLength = 32;

    /// <summary>Ayarlari kodda vererek adlandirilmis bir OpenAI uyumlu saglayici ekler.</summary>
    /// <param name="builder">AgentPrism zinciri.</param>
    /// <param name="name">
    /// Saglayici adi. <c>[a-z0-9][a-z0-9-]{0,31}</c> ile sinirlidir.
    /// <see cref="OpenAIProviderNames.ChatCompletions"/> ve
    /// <see cref="OpenAIProviderNames.Responses"/> rezervedir.
    /// </param>
    /// <param name="configure">Ayar degistirici. En az <see cref="OpenAIProviderOptions.Endpoint"/> verilmelidir.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> veya <paramref name="configure"/> <see langword="null"/> ise.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> bos, rezerve veya desene uymuyorsa.</exception>
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
    ///        .UseOpenAICompatible("openrouter", o =>
    ///        {
    ///            o.Endpoint = new Uri("https://openrouter.ai/api/v1");
    ///            o.ApiKey   = configuration["OpenRouter:ApiKey"];
    ///        })
    ///        .UseOpenAICompatible("ollama", o =>
    ///        {
    ///            o.Endpoint = new Uri("http://localhost:11434/v1");
    ///            // ApiKey YOK — yerel sunucu istemiyor
    ///        });
    /// </code>
    /// </example>
    public static IAgentPrismBuilder UseOpenAICompatible(
        this IAgentPrismBuilder builder,
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

        // Ikinci cagri ayarlari birlestirir ama saglayicilari tekrar kaydetmez;
        // UseOpenAI() ile ayni gerekce (K-025'in "AddModelProvider yeterlidir" notu).
        if (services.Any(descriptor =>
                descriptor.ServiceType == typeof(NamedProviderMarker)
                && descriptor.ImplementationInstance is NamedProviderMarker marker
                && string.Equals(marker.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return builder;
        }

        services.AddSingleton(new NamedProviderMarker(name));

        // Responses yuzeyinin acik olup olmadigini kayit ANINDA bilmemiz gerekir:
        // IModelProviderRegistry'nin hangi saglayicilari icerecegi DI kurulurken
        // belirlenir, ayarlar cozulurken degil. configure yan etkisiz bir Action
        // oldugu icin (standart Options kalibi) burada bir kez daha, atilacak bir
        // nesne uzerinde calistirmak guvenlidir.
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
    /// Ayarlari <c>AgentPrism:Providers:OpenAICompatible:{ad}</c> bolumunden okuyarak
    /// adlandirilmis bir OpenAI uyumlu saglayici ekler.
    /// </summary>
    /// <param name="builder">AgentPrism zinciri.</param>
    /// <param name="name">Saglayici adi. Bkz. <see cref="UseOpenAICompatible(IAgentPrismBuilder, string, Action{OpenAIProviderOptions})"/>.</param>
    /// <param name="configurationSection">
    /// Ayarlarin okunacagi bolum. Genellikle
    /// <c>configuration.GetSection($"{OpenAICompatibleProviderOptions.SectionName}:{name}")</c>.
    /// </param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException">Parametrelerden biri <see langword="null"/> ise.</exception>
    public static IAgentPrismBuilder UseOpenAICompatible(
        this IAgentPrismBuilder builder,
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
            // UseOpenAICompatible() sabit bir yapilandirma bolumune baglamaz — anahtar
            // cagiranin kod icinde secip verdigi rastgele bir kaynaktan gelebilir
            // (ornek: configuration["OpenRouter:ApiKey"]). Bildirilecek sabit bir yol
            // olmadigi icin teshis raporunda bu saglayici icin hicbir ConfigurationDiagnostic
            // GORUNMEZ.
            configurationSectionKey: null);
    }

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (string.Equals(name, OpenAIProviderNames.ChatCompletions, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, OpenAIProviderNames.Responses, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"'{name}' saglayici adi rezervedir. '{OpenAIProviderNames.ChatCompletions}' ve " +
                $"'{OpenAIProviderNames.Responses}' yalnizca UseOpenAI() tarafindan kullanilir; " +
                "agent tanimlarindaki ModelBinding.Provider bu adlara guvenir.",
                nameof(name));
        }

        if (!IsValidNamePattern(name))
        {
            throw new ArgumentException(
                $"'{name}' gecerli bir saglayici adi degil. Ad kucuk harf, rakam ve tire " +
                "icermeli, kucuk harf veya rakamla baslamali ve en fazla 32 karakter olmalidir " +
                "(ornek: 'openrouter', 'local-vllm').",
                nameof(name));
        }
    }

    /// <summary>
    /// Ad deseni <c>^[a-z0-9][a-z0-9-]{0,31}$</c> ile ayni kurali dogrular.
    /// </summary>
    /// <remarks>
    /// Duzenli ifade yerine elle karakter denetimi kullanilir: MA0009 (regex DoS
    /// analizi) kaynak-uretilmis duzenli ifadeleri de isaretliyor ve bu kadar basit
    /// bir desen icin bastirmaya gerek yok.
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
