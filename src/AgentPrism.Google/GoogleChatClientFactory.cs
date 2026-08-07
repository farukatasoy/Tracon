using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Ayarlardan bir Google GenAI <see cref="Client"/> kurar ve model baglantilarindan
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
/// <see cref="Client"/> bir kez kurulur ve paylasilir; HTTP baglanti havuzunu kendi
/// yonetir. Fabrika <see cref="IDisposable"/> uygular, boylece kapsayici kapanirken
/// istemci de kapanir.
/// </para>
/// </remarks>
public sealed class GoogleChatClientFactory : IDisposable
{
    private readonly Client _client;
    private readonly bool _ownsClient;
    private readonly string? _defaultModel;
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>Ayarlardan yeni bir fabrika kurar.</summary>
    /// <param name="options">Saglayici ayarlari.</param>
    /// <param name="loggerFactory">Uretilen istemcilere verilecek gunlukleyici fabrikasi.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> <see langword="null"/> ise.</exception>
    /// <exception cref="AgentPrismException"><see cref="GoogleProviderOptions.ApiKey"/> bos ise.</exception>
    public GoogleChatClientFactory(GoogleProviderOptions options, ILoggerFactory? loggerFactory = null)
        : this(CreateClient(options), options.DefaultModel, loggerFactory, ownsClient: true)
    {
    }

    /// <summary>Hazir bir istemciden yeni bir fabrika kurar.</summary>
    /// <param name="client">Kullanilacak Google GenAI istemcisi.</param>
    /// <param name="defaultModel">Model adi verilmediginde kullanilacak model.</param>
    /// <param name="loggerFactory">Uretilen istemcilere verilecek gunlukleyici fabrikasi.</param>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> <see langword="null"/> ise.</exception>
    /// <remarks>
    /// Kimlik dogrulamasini kendi yoneten kurulumlar (ornegin Vertex AI hizmet hesabi)
    /// bu kurucuyu kullanir. Bu yoldan verilen istemcinin omru <em>cagirana</em> aittir;
    /// fabrika onu kapatmaz.
    /// </remarks>
    public GoogleChatClientFactory(Client client, string? defaultModel = null, ILoggerFactory? loggerFactory = null)
        : this(client, defaultModel, loggerFactory, ownsClient: false)
    {
    }

    private GoogleChatClientFactory(Client client, string? defaultModel, ILoggerFactory? loggerFactory, bool ownsClient)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
        _ownsClient = ownsClient;
        _defaultModel = Trim(defaultModel);
        _loggerFactory = loggerFactory;
    }

    /// <summary>Verilen baglanti icin bir sohbet istemcisi uretir.</summary>
    /// <param name="binding">Model baglantisi.</param>
    /// <returns>Boru hattindan gecirilmis sohbet istemcisi.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="binding"/> <see langword="null"/> ise.</exception>
    /// <exception cref="AgentPrismException">
    /// Model adi cozulemezse veya <see cref="ModelBinding.ProviderSettings"/> icinde
    /// taninmayan bir anahtar ya da esik degeri varsa.
    /// </exception>
    public IChatClient CreateChatClient(ModelBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var model = Trim(binding.Model) ?? _defaultModel
            ?? throw new AgentPrismException(
                "Model adi bos ve varsayilan model tanimli degil. Agent tanimindaki " +
                $"{nameof(ModelBinding)}.{nameof(ModelBinding.Model)} alanini doldurun veya " +
                $"'{GoogleProviderOptions.SectionName}:{nameof(GoogleProviderOptions.DefaultModel)}' ayarini verin.");

        // Taninmayan anahtar burada reddedilir; hata AgentDefinitionCompiler
        // tarafindan AgentPrismCompilationException'a sarilir ve derleme durur.
        ModelProviderSettings.Validate(
            binding,
            GoogleProviderNames.SettingsPrefix,
            GoogleProviderNames.SupportedSettings);

        var safetySettings = GoogleSafetySettings.Read(binding);

        var thinkingBudget = ModelProviderSettings.ReadInt32(
            binding,
            GoogleProviderNames.ThinkingBudgetTokensSetting);

        var includeThoughts = ModelProviderSettings.ReadBoolean(
            binding,
            GoogleProviderNames.ThinkingIncludeThoughtsSetting);

        // Gemini butceyi [-1, 65535] araliginda ister; -1 "modele birak", 0 "kapali".
        // Aralik disi deger istegi calisma aninda reddettirir, bu yuzden derlemede yakalanir.
        if (thinkingBudget is { } budget && budget is < -1 or > 65535)
        {
            throw new AgentPrismException(
                $"'{GoogleProviderNames.ThinkingBudgetTokensSetting}' degeri [-1, 65535] araliginda olmalidir " +
                $"(-1 modele birakir, 0 dusunmeyi kapatir). Gelen deger: {budget}.");
        }

        IChatClient inner = _client.AsIChatClient(model);

        // Ayar yoksa dekorator hic eklenmez: sade yol her istekte bir ChatOptions
        // kopyasi ve bir yapilandirma nesnesi uretmemelidir.
        if (GoogleProviderSettingsChatClient.HasSettings(safetySettings, thinkingBudget, includeThoughts))
        {
            inner = new GoogleProviderSettingsChatClient(inner, safetySettings, thinkingBudget, includeThoughts);
        }

        return inner;
    }

    /// <summary>Ayarlardan bir Google GenAI istemcisi kurar.</summary>
    /// <param name="options">Saglayici ayarlari.</param>
    /// <returns>Kurulmus istemci.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> <see langword="null"/> ise.</exception>
    /// <exception cref="AgentPrismException"><see cref="GoogleProviderOptions.ApiKey"/> bos ise.</exception>
    /// <remarks>
    /// Taban adres <see cref="HttpOptions"/> uzerinden verilir. SDK'nin
    /// <c>Client.setDefaultBaseUrl</c> statik metodu bilincli olarak kullanilmaz:
    /// surec genelinde durum degistirir ve ayni uygulamada iki farkli Gemini ucu
    /// kullanilamaz hale gelirdi.
    /// </remarks>
    public static Client CreateClient(GoogleProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new AgentPrismException(
                $"{nameof(GoogleProviderOptions)}.{nameof(GoogleProviderOptions.ApiKey)} bos. " +
                "Anahtari `UseGoogle(apiKey)` cagrisinda verin veya " +
                $"'{GoogleProviderOptions.SectionName}:{nameof(GoogleProviderOptions.ApiKey)}' ayarini tanimlayin.");
        }

        HttpOptions? httpOptions = null;

        if (options.Endpoint is not null || options.ApiVersion is not null || options.Timeout is not null)
        {
            httpOptions = new HttpOptions
            {
                BaseUrl = options.Endpoint?.ToString(),
                ApiVersion = options.ApiVersion,
                Timeout = options.Timeout is { } timeout ? (int)timeout.TotalMilliseconds : null,
            };
        }

        return new Client(apiKey: options.ApiKey, httpOptions: httpOptions);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
