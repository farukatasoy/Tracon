using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Azure OpenAI icin <see cref="IModelProvider"/> uygulamasi.
/// </summary>
/// <remarks>
/// <para>
/// Saglayici, katalogda bulunmayan bir deployment adini <strong>reddetmez</strong>.
/// Deployment adlarini Azure kaynagini kuran kisi secer ve yeni bir deployment
/// acildiginda AgentPrism'in yapilandirmasinin guncellenmesi beklenmez; katalog
/// doluysa yalnizca bilgilendirme amacli bir gunluk kaydi birakilir (karar K-032).
/// </para>
/// <para>
/// Devre kesici ve icerik filtresi tespiti bu tipin <em>disinda</em>,
/// <c>ModelProviderRegistry</c> duzeyindedir; bu paket ikisini de bedava alir.
/// Azure'un kendi icerik filtresi bir yaniti bostan kestiginde
/// <c>ContentFilterDetectingChatClient</c> bunu <c>content_filtered</c> olarak
/// kaydeder — bu pakette ek kod yoktur (karar K-206).
/// </para>
/// </remarks>
public sealed class AzureOpenAIModelProvider : IModelProvider, IModelProviderHealthCheck
{
    private readonly AzureOpenAIChatClientFactory _chatClientFactory;
    private readonly ILogger<AzureOpenAIModelProvider>? _logger;
    private readonly HashSet<string> _knownDeployments;
    private readonly AzureOpenAIProviderHealthCheck? _healthCheck;

    /// <summary>Yeni bir saglayici olusturur.</summary>
    /// <param name="name">Saglayici adi. Agent tanimlarindaki <see cref="ModelBinding.Provider"/> bu degerle eslesir.</param>
    /// <param name="chatClientFactory">Sohbet istemcisi fabrikasi.</param>
    /// <param name="models">Bu saglayicinin sundugu deployment'lar.</param>
    /// <param name="logger">Gunlukleyici.</param>
    /// <param name="healthCheckOptions">
    /// Verilirse <see cref="CheckHealthAsync"/> bu ayarlardaki adres ve kimlikle
    /// <c>GET {endpoint}/openai/models</c> ucuna gider. <see langword="null"/> ise
    /// saglik durumu her zaman <see cref="ModelProviderHealthStatus.Unknown"/> doner.
    /// </param>
    /// <exception cref="ArgumentNullException">Zorunlu bagimliliklardan biri <see langword="null"/> ise.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> bos ise.</exception>
    public AzureOpenAIModelProvider(
        string name,
        AzureOpenAIChatClientFactory chatClientFactory,
        IReadOnlyList<ModelDescriptor> models,
        ILogger<AzureOpenAIModelProvider>? logger = null,
        AzureOpenAIProviderOptions? healthCheckOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(chatClientFactory);
        ArgumentNullException.ThrowIfNull(models);

        Name = name;
        Models = models;

        _chatClientFactory = chatClientFactory;
        _logger = logger;
        _knownDeployments = new HashSet<string>(models.Select(static model => model.Name), StringComparer.OrdinalIgnoreCase);
        _healthCheck = healthCheckOptions is null ? null : new AzureOpenAIProviderHealthCheck(name, healthCheckOptions);
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
        if (_knownDeployments.Count > 0
            && !string.IsNullOrWhiteSpace(binding.Model)
            && !_knownDeployments.Contains(binding.Model))
        {
            LogUnknownDeployment(binding.Model);
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

    private void LogUnknownDeployment(string deployment)
    {
        if (_logger is not null && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "'{Deployment}' adi '{Provider}' katalogunda yok; istek yine de gonderiliyor. " +
                "Azure'da bu alan MODEL adi degil DEPLOYMENT adi bekler. Tanimi kataloga eklemek " +
                "icin AgentPrism:Providers:AzureOpenAI:Models ayarini kullanin.",
                deployment,
                Name);
        }
    }
}
