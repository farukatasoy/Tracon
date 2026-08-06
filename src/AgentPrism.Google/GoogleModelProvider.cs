using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Google Gemini icin <see cref="IModelProvider"/> uygulamasi.
/// </summary>
/// <remarks>
/// <para>
/// Saglayici, katalogda bulunmayan bir model adini <strong>reddetmez</strong>
/// (karar K-032); katalog doluysa yalnizca bilgilendirme amacli bir gunluk kaydi
/// birakilir.
/// </para>
/// <para>
/// Devre kesici ve icerik filtresi tespiti bu tipin <em>disinda</em>,
/// <c>ModelProviderRegistry</c> duzeyindedir. Gemini'nin guvenlik filtresi bos yanit
/// dondurdugunde calistirma <c>content_filtered</c> hatasiyla kaydedilir.
/// </para>
/// </remarks>
public sealed class GoogleModelProvider : IModelProvider, IModelProviderHealthCheck, IModelProviderConfigurationDiagnostics
{
    private readonly GoogleChatClientFactory _chatClientFactory;
    private readonly ILogger<GoogleModelProvider>? _logger;
    private readonly HashSet<string> _knownModels;
    private readonly GoogleProviderHealthCheck? _healthCheck;
    private readonly ConfigurationDiagnostic? _configurationDiagnostic;

    /// <summary>Yeni bir saglayici olusturur.</summary>
    /// <param name="name">Saglayici adi. Agent tanimlarindaki <see cref="ModelBinding.Provider"/> bu degerle eslesir.</param>
    /// <param name="chatClientFactory">Sohbet istemcisi fabrikasi.</param>
    /// <param name="models">Bu saglayicinin sundugu modeller.</param>
    /// <param name="logger">Gunlukleyici.</param>
    /// <param name="healthCheckOptions">
    /// Verilirse <see cref="CheckHealthAsync"/> bu ayarlardaki adres ve anahtarla
    /// model listesi ucuna gider. <see langword="null"/> ise saglik durumu her zaman
    /// <see cref="ModelProviderHealthStatus.Unknown"/> doner.
    /// </param>
    /// <exception cref="ArgumentNullException">Zorunlu bagimliliklardan biri <see langword="null"/> ise.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> bos ise.</exception>
    public GoogleModelProvider(
        string name,
        GoogleChatClientFactory chatClientFactory,
        IReadOnlyList<ModelDescriptor> models,
        ILogger<GoogleModelProvider>? logger = null,
        GoogleProviderOptions? healthCheckOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(chatClientFactory);
        ArgumentNullException.ThrowIfNull(models);

        Name = name;
        Models = models;

        _chatClientFactory = chatClientFactory;
        _logger = logger;
        _knownModels = new HashSet<string>(models.Select(static model => model.Name), StringComparer.OrdinalIgnoreCase);
        _healthCheck = healthCheckOptions is null ? null : new GoogleProviderHealthCheck(name, healthCheckOptions);
        _configurationDiagnostic = BuildConfigurationDiagnostic(healthCheckOptions);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public IReadOnlyList<ModelDescriptor> Models { get; }

    /// <inheritdoc />
    public IChatClient CreateChatClient(ModelBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (_knownModels.Count > 0
            && !string.IsNullOrWhiteSpace(binding.Model)
            && !_knownModels.Contains(binding.Model))
        {
            LogUnknownModel(binding.Model);
        }

        return _chatClientFactory.CreateChatClient(binding);
    }

    /// <inheritdoc />
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

    private static ConfigurationDiagnostic? BuildConfigurationDiagnostic(GoogleProviderOptions? options)
    {
        if (options is null)
        {
            return null;
        }

        var key = $"{GoogleProviderOptions.SectionName}:ApiKey";
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
                "Model bilgisini kataloga eklemek icin AgentPrism:Providers:Google:Models ayarini kullanin.",
                model,
                Name);
        }
    }
}
