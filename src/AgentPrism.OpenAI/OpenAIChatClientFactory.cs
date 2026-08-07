using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Responses;

namespace AgentPrism;

/// <summary>
/// Ayarlardan bir <see cref="OpenAIClient"/> kurar ve model baglantilarindan
/// <see cref="IChatClient"/> uretir.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 Fabrika <strong>HAM</strong> bir istemci doner. Ortak boru hatti
/// (<c>UseFunctionInvocation()</c>, <c>UseOpenTelemetry()</c>, icerik guard'i,
/// devre kesici, ek cozme) <c>ModelProviderRegistry.CreateChatClient</c> icinde
/// kurulur — Faz 48'de oraya tasindi. Gerekce: dongu burada kurulunca defterin
/// sardigi hicbir halka tool cagri turlarini goremiyordu.
/// </para>
/// <para>
/// <see cref="OpenAIClient"/> bir kez kurulur ve paylasilir; HTTP baglanti havuzunu
/// kendi yonetir. Her derlemede yeni bir istemci kurmak havuzu parcalar.
/// </para>
/// </remarks>
public sealed class OpenAIChatClientFactory
{
    private readonly OpenAIClient _client;
    private readonly string? _defaultModel;
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>Ayarlardan yeni bir fabrika kurar.</summary>
    /// <param name="options">Saglayici ayarlari.</param>
    /// <param name="loggerFactory">Uretilen istemcilere verilecek gunlukleyici fabrikasi.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> <see langword="null"/> ise.</exception>
    /// <exception cref="AgentPrismException"><see cref="OpenAIProviderOptions.ApiKey"/> bos ise.</exception>
    public OpenAIChatClientFactory(OpenAIProviderOptions options, ILoggerFactory? loggerFactory = null)
        : this(CreateClient(options), options.DefaultModel, loggerFactory)
    {
    }

    /// <summary>Hazir bir istemciden yeni bir fabrika kurar.</summary>
    /// <param name="client">Kullanilacak OpenAI istemcisi.</param>
    /// <param name="defaultModel">Model adi verilmediginde kullanilacak model.</param>
    /// <param name="loggerFactory">Uretilen istemcilere verilecek gunlukleyici fabrikasi.</param>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> <see langword="null"/> ise.</exception>
    /// <remarks>
    /// Kimlik dogrulamasini kendi yoneten kurulumlar (ornegin token yenileyen bir
    /// kimlik saglayici) bu kurucuyu kullanir.
    /// </remarks>
    public OpenAIChatClientFactory(OpenAIClient client, string? defaultModel = null, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
        _defaultModel = Trim(defaultModel);
        _loggerFactory = loggerFactory;
    }

    /// <summary>Verilen baglanti icin bir sohbet istemcisi uretir.</summary>
    /// <param name="binding">Model baglantisi.</param>
    /// <param name="apiSurface">Kullanilacak OpenAI API yuzeyi.</param>
    /// <returns>Boru hattindan gecirilmis sohbet istemcisi.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="binding"/> <see langword="null"/> ise.</exception>
    /// <exception cref="AgentPrismException">Model adi cozulemezse.</exception>
    public IChatClient CreateChatClient(ModelBinding binding, OpenAIApiSurface apiSurface)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var model = Trim(binding.Model) ?? _defaultModel
            ?? throw new AgentPrismException(
                "Model adi bos ve varsayilan model tanimli degil. Agent tanimindaki " +
                $"{nameof(ModelBinding)}.{nameof(ModelBinding.Model)} alanini doldurun veya " +
                $"'{OpenAIProviderOptions.SectionName}:{nameof(OpenAIProviderOptions.DefaultModel)}' ayarini verin.");

        return CreateInnerChatClient(model, apiSurface);
    }

    /// <summary>Ayarlardan bir OpenAI istemcisi kurar.</summary>
    /// <param name="options">Saglayici ayarlari.</param>
    /// <returns>Kurulmus istemci.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> <see langword="null"/> ise.</exception>
    /// <exception cref="AgentPrismException"><see cref="OpenAIProviderOptions.ApiKey"/> bos ise.</exception>
    public static OpenAIClient CreateClient(OpenAIProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new AgentPrismException(
                $"{nameof(OpenAIProviderOptions)}.{nameof(OpenAIProviderOptions.ApiKey)} bos. " +
                $"Anahtari `UseOpenAI(apiKey)` cagrisinda verin veya " +
                $"'{OpenAIProviderOptions.SectionName}:{nameof(OpenAIProviderOptions.ApiKey)}' ayarini tanimlayin.");
        }

        var clientOptions = new OpenAIClientOptions();

        if (options.Endpoint is not null)
        {
            clientOptions.Endpoint = options.Endpoint;
        }

        if (!string.IsNullOrWhiteSpace(options.Organization))
        {
            clientOptions.OrganizationId = options.Organization;
        }

        if (options.Timeout is { } timeout)
        {
            clientOptions.NetworkTimeout = timeout;
        }

        return new OpenAIClient(new ApiKeyCredential(options.ApiKey), clientOptions);
    }

    private IChatClient CreateInnerChatClient(string model, OpenAIApiSurface apiSurface)
    {
        if (apiSurface is OpenAIApiSurface.ChatCompletions)
        {
            return _client.GetChatClient(model).AsIChatClient();
        }

        // Sunucu tarafi depolama BILEREK kapali. Acik birakilirsa OpenAI bir konusma
        // kimligi dondurur ve MAF hata verir: "Only ConversationId or ChatHistoryProvider
        // may be used, but not both." AgentPrism'in kalicilik katmani her agent'a bir
        // ChatHistoryProvider baglar, dolayisiyla iki yol ayni anda calisamaz.
        // Olculdu: acikken UsePostgreSql() ile birlikte calisma aninda InvalidOperationException.
        //
        // OPENAI001 / MAAI001: hem OpenAI kitapligi hem Microsoft Agent Framework bu
        // yolu "evaluation purposes only" olarak isaretliyor ve TreatWarningsAsErrors
        // ile build'i kiriyor. Bastirma bilincli bir karardir: Responses kullanimi
        // yalnizca bu iki satirdadir, boylece API degisirse tek nokta guncellenir.
        // Gerekce: docs/KARARLAR.md, kararlar K-030 ve K-031.
#pragma warning disable OPENAI001, MAAI001
        return _client.GetResponsesClient().AsIChatClientWithStoredOutputDisabled(model);
#pragma warning restore OPENAI001, MAAI001
    }

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
