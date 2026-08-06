using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// OpenAI icin <see cref="IModelProvider"/> uygulamasi.
/// </summary>
/// <remarks>
/// <para>
/// <c>UseOpenAI()</c> bu tipten <strong>iki</strong> ornek kaydeder:
/// <see cref="OpenAIProviderNames.ChatCompletions"/> ve
/// <see cref="OpenAIProviderNames.Responses"/>. Ikisi ayni
/// <see cref="OpenAIChatClientFactory"/> ornegini, dolayisiyla ayni HTTP baglanti
/// havuzunu paylasir; yalnizca <see cref="ApiSurface"/> degerleri farklidir.
/// </para>
/// <para>
/// Saglayici, katalogda bulunmayan bir model adini <strong>reddetmez</strong>.
/// OpenAI yeni bir model yayinladiginda AgentPrism'in yeni bir surumu beklenmez;
/// katalog doluysa yalnizca bilgilendirme amacli bir gunluk kaydi birakilir.
/// AgentPrism yerlesik model listesi tasimadigi icin (karar K-032) katalogun bos
/// olmasi normaldir; o durumda gunluk yazilmaz.
/// </para>
/// </remarks>
public sealed class OpenAIModelProvider : IModelProvider, IModelProviderHealthCheck, IModelProviderConfigurationDiagnostics
{
    private readonly OpenAIChatClientFactory _chatClientFactory;
    private readonly ILogger<OpenAIModelProvider>? _logger;
    private readonly HashSet<string> _knownModels;
    private readonly OpenAIProviderHealthCheck? _healthCheck;
    private readonly ConfigurationDiagnostic? _configurationDiagnostic;

    /// <summary>Yeni bir saglayici olusturur.</summary>
    /// <param name="name">Saglayici adi. Agent tanimlarindaki <see cref="ModelBinding.Provider"/> bu degerle eslesir.</param>
    /// <param name="apiSurface">Kullanilacak OpenAI API yuzeyi.</param>
    /// <param name="chatClientFactory">Sohbet istemcisi fabrikasi.</param>
    /// <param name="models">Bu saglayicinin sundugu modeller.</param>
    /// <param name="logger">Gunlukleyici.</param>
    /// <param name="healthCheckOptions">
    /// Verilirse <see cref="CheckHealthAsync"/> bu ayarlardaki adres ve anahtarla
    /// <c>GET {endpoint}/models</c> ucuna gider. <see langword="null"/> ise saglik
    /// durumu her zaman <see cref="ModelProviderHealthStatus.Unknown"/> doner.
    /// </param>
    /// <param name="configurationSectionKey">
    /// <see cref="GetConfigurationDiagnostic"/>'in bildirecegi sabit yapilandirma
    /// bolumu. Varsayilan <see cref="OpenAIProviderOptions.SectionName"/> —
    /// <c>UseOpenAI()</c> icindir. <c>UseOpenAICompatible()</c> anahtari kod icinde
    /// serbestce verdigi (sabit bir bolum yolu olmadigi) icin <see langword="null"/>
    /// gecer; bu durumda hicbir <see cref="ConfigurationDiagnostic"/> bildirilmez.
    /// </param>
    /// <exception cref="ArgumentNullException">Zorunlu bagimliliklardan biri <see langword="null"/> ise.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> bos ise.</exception>
    public OpenAIModelProvider(
        string name,
        OpenAIApiSurface apiSurface,
        OpenAIChatClientFactory chatClientFactory,
        IReadOnlyList<ModelDescriptor> models,
        ILogger<OpenAIModelProvider>? logger = null,
        OpenAIProviderOptions? healthCheckOptions = null,
        string? configurationSectionKey = OpenAIProviderOptions.SectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(chatClientFactory);
        ArgumentNullException.ThrowIfNull(models);

        Name = name;
        ApiSurface = apiSurface;
        Models = models;

        _chatClientFactory = chatClientFactory;
        _logger = logger;
        _knownModels = new HashSet<string>(models.Select(static model => model.Name), StringComparer.OrdinalIgnoreCase);
        _healthCheck = healthCheckOptions is null ? null : new OpenAIProviderHealthCheck(name, healthCheckOptions);
        _configurationDiagnostic = BuildConfigurationDiagnostic(healthCheckOptions, configurationSectionKey);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>Bu saglayicinin kullandigi OpenAI API yuzeyi.</summary>
    public OpenAIApiSurface ApiSurface { get; }

    /// <inheritdoc />
    public IReadOnlyList<ModelDescriptor> Models { get; }

    /// <inheritdoc />
    public IChatClient CreateChatClient(ModelBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        // Katalog bossa karsilastirilacak bir sey yoktur; her cagride gunluk yazmak
        // gurultu olurdu. AgentPrism yerlesik model listesi tasimaz (karar K-032),
        // bu yuzden bos katalog normal durumdur.
        if (_knownModels.Count > 0
            && !string.IsNullOrWhiteSpace(binding.Model)
            && !_knownModels.Contains(binding.Model))
        {
            LogUnknownModel(binding.Model);
        }

        return _chatClientFactory.CreateChatClient(binding, ApiSurface);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Saglik denetimi <see cref="_chatClientFactory"/>'yi kullanmaz — ayri, hafif bir
    /// HTTP GET yapar. Bkz. <see cref="OpenAIProviderHealthCheck"/>.
    /// </remarks>
    public ValueTask<ModelProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
        => _healthCheck?.CheckHealthAsync(cancellationToken)
            ?? ValueTask.FromResult(new ModelProviderHealth
            {
                ProviderName = Name,
                Status = ModelProviderHealthStatus.Unknown,
                CheckedAt = DateTimeOffset.UtcNow,
            });

    /// <inheritdoc />
    public ConfigurationDiagnostic? GetConfigurationDiagnostic() => _configurationDiagnostic;

    private static ConfigurationDiagnostic? BuildConfigurationDiagnostic(OpenAIProviderOptions? options, string? sectionKey)
    {
        if (options is null || sectionKey is null)
        {
            return null;
        }

        var key = $"{sectionKey}:ApiKey";
        var resolved = !string.IsNullOrWhiteSpace(options.ApiKey);

        return new ConfigurationDiagnostic
        {
            Key = key,
            Resolved = resolved,
            Hint = resolved ? null : $"dotnet user-secrets set \"{key}\" \"<anahtar>\"",
        };
    }

    private void LogUnknownModel(string model)
    {
        if (_logger is not null && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "'{Model}' modeli '{Provider}' katalogunda yok; istek yine de gonderiliyor. " +
                "Model bilgisini kataloga eklemek icin AgentPrism:Providers:OpenAI:Models ayarini kullanin.",
                model,
                Name);
        }
    }
}
