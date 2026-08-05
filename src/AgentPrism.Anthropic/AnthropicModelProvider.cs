using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Anthropic (Claude) icin <see cref="IModelProvider"/> uygulamasi.
/// </summary>
/// <remarks>
/// <para>
/// Saglayici, katalogda bulunmayan bir model adini <strong>reddetmez</strong>.
/// Anthropic yeni bir model yayinladiginda AgentPrism'in yeni bir surumu beklenmez;
/// katalog doluysa yalnizca bilgilendirme amacli bir gunluk kaydi birakilir
/// (karar K-032).
/// </para>
/// <para>
/// Devre kesici ve icerik filtresi tespiti bu tipin <em>disinda</em>,
/// <c>ModelProviderRegistry</c> duzeyindedir; bu paket ikisini de bedava alir.
/// </para>
/// </remarks>
public sealed class AnthropicModelProvider : IModelProvider, IModelProviderHealthCheck
{
    private readonly AnthropicChatClientFactory _chatClientFactory;
    private readonly ILogger<AnthropicModelProvider>? _logger;
    private readonly HashSet<string> _knownModels;
    private readonly AnthropicProviderHealthCheck? _healthCheck;

    /// <summary>Yeni bir saglayici olusturur.</summary>
    /// <param name="name">Saglayici adi. Agent tanimlarindaki <see cref="ModelBinding.Provider"/> bu degerle eslesir.</param>
    /// <param name="chatClientFactory">Sohbet istemcisi fabrikasi.</param>
    /// <param name="models">Bu saglayicinin sundugu modeller.</param>
    /// <param name="logger">Gunlukleyici.</param>
    /// <param name="healthCheckOptions">
    /// Verilirse <see cref="CheckHealthAsync"/> bu ayarlardaki adres ve anahtarla
    /// <c>GET {endpoint}/models</c> ucuna gider. <see langword="null"/> ise saglik
    /// durumu her zaman <see cref="ModelProviderHealthStatus.Unknown"/> doner.
    /// </param>
    /// <exception cref="ArgumentNullException">Zorunlu bagimliliklardan biri <see langword="null"/> ise.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> bos ise.</exception>
    public AnthropicModelProvider(
        string name,
        AnthropicChatClientFactory chatClientFactory,
        IReadOnlyList<ModelDescriptor> models,
        ILogger<AnthropicModelProvider>? logger = null,
        AnthropicProviderOptions? healthCheckOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(chatClientFactory);
        ArgumentNullException.ThrowIfNull(models);

        Name = name;
        Models = models;

        _chatClientFactory = chatClientFactory;
        _logger = logger;
        _knownModels = new HashSet<string>(models.Select(static model => model.Name), StringComparer.OrdinalIgnoreCase);
        _healthCheck = healthCheckOptions is null ? null : new AnthropicProviderHealthCheck(name, healthCheckOptions);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public IReadOnlyList<ModelDescriptor> Models { get; }

    /// <inheritdoc />
    public IChatClient CreateChatClient(ModelBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        // Katalog bossa karsilastirilacak bir sey yoktur; her cagride gunluk yazmak
        // gurultu olurdu (karar K-032).
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

    private void LogUnknownModel(string model)
    {
        if (_logger is not null && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "'{Model}' modeli '{Provider}' katalogunda yok; istek yine de gonderiliyor. " +
                "Model bilgisini kataloga eklemek icin AgentPrism:Providers:Anthropic:Models ayarini kullanin.",
                model,
                Name);
        }
    }
}
