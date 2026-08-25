using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// <see cref="IModelProvider"/> implementation for Google Gemini.
/// </summary>
/// <remarks>
/// <para>
/// The provider <strong>does not reject</strong> a model name absent from the
/// catalog; when the catalog is non-empty, only an informational
/// log entry is left.
/// </para>
/// <para>
/// Circuit breaking and content-filter detection live <em>outside</em> this type,
/// at the <c>ModelProviderRegistry</c> level. When Gemini's safety filter returns
/// an empty response, the run is recorded with a <c>content_filtered</c> error.
/// </para>
/// </remarks>
internal sealed class GoogleModelProvider : ITenantCredentialModelProvider, IModelProviderHealthCheck, IModelProviderConfigurationDiagnostics
{
    private readonly GoogleChatClientFactory _chatClientFactory;
    private readonly ILogger<GoogleModelProvider>? _logger;
    private readonly HashSet<string> _knownModels;
    private readonly GoogleProviderHealthCheck? _healthCheck;
    private readonly ConfigurationDiagnostic? _configurationDiagnostic;
    private readonly GoogleProviderOptions? _baseOptions;

    // Phase 65 (BYOK). See OpenAIModelProvider's remark on _credentialFactories.
    // 🚨 GoogleChatClientFactory is IDisposable (owns an HttpClient); cached
    // entries here are never disposed, the same bounded, admin-controlled
    // trade-off ProviderCredentialClientCache documents.
    private readonly EgressSocketGuard? _egressGuard;
    private readonly ProviderCredentialClientCache<GoogleChatClientFactory> _credentialFactories = new();

    /// <summary>Creates a new provider.</summary>
    /// <param name="name">Provider name. Matches <see cref="ModelBinding.Provider"/> in agent definitions.</param>
    /// <param name="chatClientFactory">Chat client factory.</param>
    /// <param name="models">Models this provider offers.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="healthCheckOptions">
    /// When given, <see cref="CheckHealthAsync"/> calls the model list endpoint with
    /// the address and key from these settings. When <see langword="null"/>, health
    /// status is always <see cref="ModelProviderHealthStatus.Unknown"/>.
    /// </param>
    /// <param name="egressGuard">
    /// The outbound network guard. Attached only to a client built from a
    /// tenant-supplied endpoint override; when <see langword="null"/>, such an
    /// override is not guarded.
    /// </param>
    /// <exception cref="ArgumentNullException">One of the required dependencies is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    public GoogleModelProvider(
        string name,
        GoogleChatClientFactory chatClientFactory,
        IReadOnlyList<ModelDescriptor> models,
        ILogger<GoogleModelProvider>? logger = null,
        GoogleProviderOptions? healthCheckOptions = null,
        EgressSocketGuard? egressGuard = null)
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
        _baseOptions = healthCheckOptions;
        _egressGuard = egressGuard;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public IReadOnlyList<ModelDescriptor> Models { get; }

    /// <inheritdoc />
    public IChatClient CreateChatClient(ModelBinding binding)
        => CreateChatClientCore(binding, credential: null);

    /// <inheritdoc />
    public IChatClient CreateChatClient(ModelBinding binding, ModelProviderCredential credential)
        => CreateChatClientCore(binding, credential);

    private IChatClient CreateChatClientCore(ModelBinding binding, ModelProviderCredential? credential)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (_knownModels.Count > 0
            && !string.IsNullOrWhiteSpace(binding.Model)
            && !_knownModels.Contains(binding.Model))
        {
            LogUnknownModel(binding.Model);
        }

        var factory = credential is null
            ? _chatClientFactory
            : _credentialFactories.GetOrAdd(credential, BuildCredentialFactory);

        return factory.CreateChatClient(binding);
    }

    /// <summary>Builds a per-tenant client factory from a resolved credential (BYOK).</summary>
    /// <remarks>See <c>OpenAIModelProvider.BuildCredentialFactory</c> for the endpoint fallback rationale.</remarks>
    private GoogleChatClientFactory BuildCredentialFactory(ModelProviderCredential credential)
    {
        var options = new GoogleProviderOptions
        {
            ApiKey = credential.ApiKey,
            DefaultModel = _baseOptions?.DefaultModel,
            ApiVersion = _baseOptions?.ApiVersion,
            Timeout = _baseOptions?.Timeout,
        };

        var overrideEndpoint = credential.Endpoint is { Length: > 0 } endpoint
            && Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
                ? endpointUri
                : null;

        options.Endpoint = overrideEndpoint ?? _baseOptions?.Endpoint;

        return GoogleChatClientFactory.FromClient(
            GoogleChatClientFactory.CreateClient(options, GuardFor(overrideEndpoint)),
            options.DefaultModel);
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
            Hint = resolved ? null : $"dotnet user-secrets set \"{key}\" \"<key>\"",
        };
    }

    private void LogUnknownModel(string model)
    {
        if (_logger is not null && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Model '{Model}' is not in the '{Provider}' catalog; sending the request anyway. " +
                "Use the AgentPrism:Providers:Google:Models setting to add model info to the catalog.",
                model,
                Name);
        }
    }

    /// <summary>
    /// Returns the guard to attach to a per-tenant client, or
    /// <see langword="null"/> when none is needed.
    /// </summary>
    /// <remarks>
    /// The guard is attached <strong>only</strong> when the endpoint came
    /// from the tenant's binding. A setup-time endpoint is the operator's own
    /// decision and is written in code — guarding it would break sovereign
    /// cloud and internal-proxy setups that are deliberately private. A
    /// tenant-supplied override is outside input and is guarded.
    /// </remarks>
    private EgressSocketGuard? GuardFor(Uri? tenantSuppliedEndpoint)
        => tenantSuppliedEndpoint is null ? null : _egressGuard;
}
