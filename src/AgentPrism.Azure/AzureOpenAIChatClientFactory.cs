using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Ayarlardan bir <see cref="AzureOpenAIClient"/> kurar ve model baglantilarindan
/// <see cref="IChatClient"/> uretir.
/// </summary>
/// <remarks>
/// <para>
/// Uretilen her istemci <c>AgentPrism.OpenAI</c> ile ayni boru hattindan gecer:
/// <c>UseFunctionInvocation()</c> tool cagri dongusunu Microsoft Agent Framework'e
/// birakir, <c>UseOpenTelemetry()</c> span'leri
/// <see cref="AgentPrismDiagnostics.ActivitySourceName"/> kaynagi altinda uretir.
/// </para>
/// <para>
/// <see cref="AzureOpenAIClient"/> bir kez kurulur ve paylasilir; HTTP baglanti
/// havuzunu kendi yonetir. Her derlemede yeni bir istemci kurmak havuzu parcalar.
/// </para>
/// <para>
/// 🚨 <strong>Azure'da cagrilan sey deployment adidir, model adi degil.</strong>
/// <see cref="ModelBinding.Model"/> alani bu saglayicida deployment adini tasir ve
/// istegin yoluna girer:
/// <c>POST {endpoint}/openai/deployments/{deployment}/chat/completions</c>.
/// Ayni model bir kaynakta <c>uretim-gpt</c>, digerinde <c>gpt-mini-tr</c> adiyla
/// konuslandirilmis olabilir; adi kaynagi kuran kisi secer.
/// </para>
/// <para>
/// Saglayici <see cref="ModelBinding.ProviderSettings"/> icinde <strong>hicbir
/// anahtar</strong> desteklemez. Azure'un sohbet istegine ekleyebilecegi ozel
/// alanlar <c>AzureChatExtensions</c> uzerinden yazilir ve o uzantilar
/// kullandigimiz OpenAI SDK surumuyle calisma aninda kirilir (karar K-211).
/// Tanimli bir anahtar geldiginde derleme acik bir hatayla durur.
/// </para>
/// </remarks>
public sealed class AzureOpenAIChatClientFactory
{
    private readonly AzureOpenAIClient _client;
    private readonly string? _defaultDeployment;
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>Ayarlardan yeni bir fabrika kurar.</summary>
    /// <param name="options">Saglayici ayarlari.</param>
    /// <param name="loggerFactory">Uretilen istemcilere verilecek gunlukleyici fabrikasi.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> <see langword="null"/> ise.</exception>
    /// <exception cref="AgentPrismException">
    /// <see cref="AzureOpenAIProviderOptions.Endpoint"/> bos ise veya ne API anahtari
    /// ne de kimlik fabrikasi verilmisse.
    /// </exception>
    public AzureOpenAIChatClientFactory(AzureOpenAIProviderOptions options, ILoggerFactory? loggerFactory = null)
        : this(CreateClient(options), options.DefaultDeployment, loggerFactory)
    {
    }

    /// <summary>Hazir bir istemciden yeni bir fabrika kurar.</summary>
    /// <param name="client">Kullanilacak Azure OpenAI istemcisi.</param>
    /// <param name="defaultDeployment">Deployment adi verilmediginde kullanilacak ad.</param>
    /// <param name="loggerFactory">Uretilen istemcilere verilecek gunlukleyici fabrikasi.</param>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> <see langword="null"/> ise.</exception>
    /// <remarks>
    /// Istemciyi kendi kuran kurulumlar bu kurucuyu kullanir: ozel bir
    /// <c>AzureOpenAIClientOptions</c>, egemen bir bulut ya da elle yonetilen bir
    /// HTTP boru hatti gerektiginde tek kacis kapisi budur.
    /// </remarks>
    public AzureOpenAIChatClientFactory(
        AzureOpenAIClient client,
        string? defaultDeployment = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
        _defaultDeployment = Trim(defaultDeployment);
        _loggerFactory = loggerFactory;
    }

    /// <summary>Verilen baglanti icin bir sohbet istemcisi uretir.</summary>
    /// <param name="binding">Model baglantisi. <see cref="ModelBinding.Model"/> deployment adidir.</param>
    /// <returns>Boru hattindan gecirilmis sohbet istemcisi.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="binding"/> <see langword="null"/> ise.</exception>
    /// <exception cref="AgentPrismException">
    /// Deployment adi cozulemezse veya <see cref="ModelBinding.ProviderSettings"/>
    /// icinde herhangi bir anahtar varsa.
    /// </exception>
    public IChatClient CreateChatClient(ModelBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var deployment = Trim(binding.Model) ?? _defaultDeployment
            ?? throw new AgentPrismException(
                "Deployment adi bos ve varsayilan deployment tanimli degil. Azure OpenAI'da " +
                $"{nameof(ModelBinding)}.{nameof(ModelBinding.Model)} alani bir MODEL adi degil, " +
                "Azure kaynaginizda tanimli bir DEPLOYMENT adi bekler. Alani doldurun veya " +
                $"'{AzureOpenAIProviderOptions.SectionName}:{nameof(AzureOpenAIProviderOptions.DefaultDeployment)}' " +
                "ayarini verin.");

        // Bu saglayici hicbir ek ayar desteklemez; gelen her anahtar burada
        // reddedilir ve hata AgentDefinitionCompiler tarafindan
        // AgentPrismCompilationException'a sarilir. Sessizce yok saymak,
        // kullanicinin bekledigi davranisi almamasina yol acardi (K-208).
        ModelProviderSettings.Validate(
            binding,
            AzureOpenAIProviderNames.SettingsPrefix,
            AzureOpenAIProviderNames.SupportedSettings);

        return _client.GetChatClient(deployment)
            .AsIChatClient()
            .AsBuilder()
            .UseFunctionInvocation(_loggerFactory)
            .UseOpenTelemetry(_loggerFactory, AgentPrismDiagnostics.ActivitySourceName)
            .Build();
    }

    /// <summary>Ayarlardan bir Azure OpenAI istemcisi kurar.</summary>
    /// <param name="options">Saglayici ayarlari.</param>
    /// <returns>Kurulmus istemci.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> <see langword="null"/> ise.</exception>
    /// <exception cref="AgentPrismException">
    /// <see cref="AzureOpenAIProviderOptions.Endpoint"/> bos ise veya ne API anahtari
    /// ne de kimlik fabrikasi verilmisse.
    /// </exception>
    public static AzureOpenAIClient CreateClient(AzureOpenAIProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Endpoint is null)
        {
            throw new AgentPrismException(
                $"{nameof(AzureOpenAIProviderOptions)}.{nameof(AzureOpenAIProviderOptions.Endpoint)} bos. " +
                "Azure OpenAI'in tek bir genel adresi yoktur; kaynaginizin adresini " +
                "`UseAzureOpenAI(endpoint, ...)` cagrisinda veya " +
                $"'{AzureOpenAIProviderOptions.SectionName}:{nameof(AzureOpenAIProviderOptions.Endpoint)}' " +
                "ayarinda verin.");
        }

        var clientOptions = new AzureOpenAIClientOptions();

        if (options.Timeout is { } timeout)
        {
            clientOptions.NetworkTimeout = timeout;
        }

        if (!string.IsNullOrWhiteSpace(options.Audience))
        {
            clientOptions.Audience = new AzureOpenAIAudience(options.Audience);
        }

        // Kimlik fabrikasi anahtari EZER: ikisi de verilmisse yonetilen kimlik
        // daha guvenli olandir ve sessizce anahtara dusmek yanlis olurdu.
        if (options.CredentialFactory is { } credentialFactory)
        {
            var credential = credentialFactory()
                ?? throw new AgentPrismException(
                    $"{nameof(AzureOpenAIProviderOptions)}.{nameof(AzureOpenAIProviderOptions.CredentialFactory)} " +
                    "null dondurdu. Fabrika her zaman bir kimlik uretmelidir.");

            return new AzureOpenAIClient(options.Endpoint, credential, clientOptions);
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new AgentPrismException(
                $"{nameof(AzureOpenAIProviderOptions)}.{nameof(AzureOpenAIProviderOptions.ApiKey)} bos ve " +
                $"{nameof(AzureOpenAIProviderOptions.CredentialFactory)} tanimli degil. " +
                "Anahtari `UseAzureOpenAI(endpoint, apiKey)` cagrisinda verin, " +
                $"'{AzureOpenAIProviderOptions.SectionName}:{nameof(AzureOpenAIProviderOptions.ApiKey)}' " +
                "ayarini tanimlayin veya yonetilen kimlik icin " +
                $"{nameof(AzureOpenAIProviderOptions.CredentialFactory)} verin.");
        }

        return new AzureOpenAIClient(options.Endpoint, new ApiKeyCredential(options.ApiKey), clientOptions);
    }

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
