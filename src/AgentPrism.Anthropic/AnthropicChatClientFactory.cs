using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Ayarlardan bir <see cref="AnthropicClient"/> kurar ve model baglantilarindan
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
/// <see cref="AnthropicClient"/> bir kez kurulur ve paylasilir; HTTP baglanti
/// havuzunu kendi yonetir. Her derlemede yeni bir istemci kurmak havuzu parcalar.
/// </para>
/// <para>
/// 🚨 Anthropic Messages API'si <c>max_tokens</c> alanini <strong>zorunlu</strong>
/// tutar. <see cref="ModelBinding.MaxOutputTokens"/> bos birakilirsa
/// <see cref="AnthropicProviderOptions.DefaultMaxOutputTokens"/> kullanilir.
/// </para>
/// </remarks>
public sealed class AnthropicChatClientFactory
{
    private readonly IAnthropicClient _client;
    private readonly string? _defaultModel;
    private readonly int _defaultMaxOutputTokens;
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>Ayarlardan yeni bir fabrika kurar.</summary>
    /// <param name="options">Saglayici ayarlari.</param>
    /// <param name="loggerFactory">Uretilen istemcilere verilecek gunlukleyici fabrikasi.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> <see langword="null"/> ise.</exception>
    /// <exception cref="AgentPrismException"><see cref="AnthropicProviderOptions.ApiKey"/> bos ise.</exception>
    public AnthropicChatClientFactory(AnthropicProviderOptions options, ILoggerFactory? loggerFactory = null)
        : this(CreateClient(options), options.DefaultModel, options.DefaultMaxOutputTokens, loggerFactory)
    {
    }

    /// <summary>Hazir bir istemciden yeni bir fabrika kurar.</summary>
    /// <param name="client">Kullanilacak Anthropic istemcisi.</param>
    /// <param name="defaultModel">Model adi verilmediginde kullanilacak model.</param>
    /// <param name="defaultMaxOutputTokens">
    /// <see cref="ModelBinding.MaxOutputTokens"/> verilmediginde kullanilacak ust sinir.
    /// </param>
    /// <param name="loggerFactory">Uretilen istemcilere verilecek gunlukleyici fabrikasi.</param>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> <see langword="null"/> ise.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="defaultMaxOutputTokens"/> pozitif degilse.</exception>
    /// <remarks>
    /// Kimlik dogrulamasini kendi yoneten kurulumlar (ornegin Bedrock/Vertex kimligi
    /// veya token yenileyen bir saglayici) bu kurucuyu kullanir.
    /// </remarks>
    public AnthropicChatClientFactory(
        IAnthropicClient client,
        string? defaultModel = null,
        int defaultMaxOutputTokens = 4096,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(defaultMaxOutputTokens);

        _client = client;
        _defaultModel = Trim(defaultModel);
        _defaultMaxOutputTokens = defaultMaxOutputTokens;
        _loggerFactory = loggerFactory;
    }

    /// <summary>Verilen baglanti icin bir sohbet istemcisi uretir.</summary>
    /// <param name="binding">Model baglantisi.</param>
    /// <returns>Boru hattindan gecirilmis sohbet istemcisi.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="binding"/> <see langword="null"/> ise.</exception>
    /// <exception cref="AgentPrismException">
    /// Model adi cozulemezse veya <see cref="ModelBinding.ProviderSettings"/> icinde
    /// taninmayan bir anahtar varsa.
    /// </exception>
    public IChatClient CreateChatClient(ModelBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var model = Trim(binding.Model) ?? _defaultModel
            ?? throw new AgentPrismException(
                "Model adi bos ve varsayilan model tanimli degil. Agent tanimindaki " +
                $"{nameof(ModelBinding)}.{nameof(ModelBinding.Model)} alanini doldurun veya " +
                $"'{AnthropicProviderOptions.SectionName}:{nameof(AnthropicProviderOptions.DefaultModel)}' ayarini verin.");

        // Taninmayan anahtar burada reddedilir; hata AgentDefinitionCompiler
        // tarafindan AgentPrismCompilationException'a sarilir ve derleme durur.
        ModelProviderSettings.Validate(
            binding,
            AnthropicProviderNames.SettingsPrefix,
            AnthropicProviderNames.SupportedSettings);

        var promptCaching = ModelProviderSettings.ReadBoolean(
            binding,
            AnthropicProviderNames.PromptCachingSetting) ?? false;

        var thinkingBudget = ModelProviderSettings.ReadInt32(
            binding,
            AnthropicProviderNames.ThinkingBudgetTokensSetting);

        if (thinkingBudget is { } budget && budget <= 0)
        {
            throw new AgentPrismException(
                $"'{AnthropicProviderNames.ThinkingBudgetTokensSetting}' must be greater than zero. " +
                $"Actual value: {budget}.");
        }

        IChatClient inner = _client.AsIChatClient(model, _defaultMaxOutputTokens);

        // Ayar yoksa dekorator hic eklenmez: sade yol her istekte bir ChatOptions
        // kopyasi ve bir ham govde uretmemelidir.
        if (AnthropicProviderSettingsChatClient.HasSettings(promptCaching, thinkingBudget))
        {
            inner = new AnthropicProviderSettingsChatClient(
                inner,
                model,
                _defaultMaxOutputTokens,
                promptCaching,
                thinkingBudget);
        }

        return inner;
    }

    /// <summary>Ayarlardan bir Anthropic istemcisi kurar.</summary>
    /// <param name="options">Saglayici ayarlari.</param>
    /// <returns>Kurulmus istemci.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> <see langword="null"/> ise.</exception>
    /// <exception cref="AgentPrismException"><see cref="AnthropicProviderOptions.ApiKey"/> bos ise.</exception>
    public static AnthropicClient CreateClient(AnthropicProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new AgentPrismException(
                $"{nameof(AnthropicProviderOptions)}.{nameof(AnthropicProviderOptions.ApiKey)} bos. " +
                "Anahtari `UseAnthropic(apiKey)` cagrisinda verin veya " +
                $"'{AnthropicProviderOptions.SectionName}:{nameof(AnthropicProviderOptions.ApiKey)}' ayarini tanimlayin.");
        }

        var clientOptions = new Anthropic.Core.ClientOptions
        {
            ApiKey = options.ApiKey,
            Timeout = options.Timeout,
            MaxRetries = options.MaxRetries,
        };

        // BaseUrl null kabul etmez; verilmediginde SDK'nin kendi varsayilani
        // (https://api.anthropic.com) gecerli kalmalidir.
        if (options.Endpoint is { } endpoint)
        {
            clientOptions.BaseUrl = endpoint.ToString();
        }

        return new AnthropicClient(clientOptions);
    }

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
