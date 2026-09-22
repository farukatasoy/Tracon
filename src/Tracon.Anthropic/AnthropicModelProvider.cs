using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// <see cref="IModelProvider"/> implementation for Anthropic (Claude).
/// </summary>
/// <remarks>
/// <para>
/// The provider does <strong>not reject</strong> a model name that is absent from
/// the catalog. Tracon does not need a new release when Anthropic ships a new
/// model; when the catalog is non-empty, only an informational log entry is
/// written.
/// </para>
/// <para>
/// Circuit-breaker and content-filter detection live <em>outside</em> this type,
/// at the <c>ModelProviderRegistry</c> level; this package gets both for free.
/// </para>
/// </remarks>
internal sealed class AnthropicModelProvider : ITenantCredentialModelProvider, IModelProviderHealthCheck, IModelProviderConfigurationDiagnostics
{
    private readonly AnthropicChatClientFactory _chatClientFactory;
    private readonly ILogger<AnthropicModelProvider>? _logger;
    private readonly AnthropicProviderHealthCheck? _healthCheck;
    private readonly ConfigurationDiagnostic? _configurationDiagnostic;
    private readonly AnthropicProviderOptions? _baseOptions;

    // Known-model set, BYOK caches and the tenant endpoint guard rule (shared source).
    private readonly ModelProviderCore<AnthropicChatClientFactory> _core;

    /// <summary>Creates a new provider.</summary>
    /// <param name="name">Provider name. Matches <see cref="ModelBinding.Provider"/> in agent definitions.</param>
    /// <param name="chatClientFactory">Chat client factory.</param>
    /// <param name="models">The models this provider offers.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="healthCheckOptions">
    /// When given, <see cref="CheckHealthAsync"/> calls <c>GET {endpoint}/models</c>
    /// using the address and key in these settings. When <see langword="null"/>,
    /// health status always returns <see cref="ModelProviderHealthStatus.Unknown"/>.
    /// </param>
    /// <param name="egressGuard">
    /// The outbound network guard. Attached only to a client built from a
    /// tenant-supplied endpoint override; when <see langword="null"/>, such an
    /// override is not guarded.
    /// </param>
    /// <exception cref="ArgumentNullException">One of the required dependencies is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    public AnthropicModelProvider(
        string name,
        AnthropicChatClientFactory chatClientFactory,
        IReadOnlyList<ModelDescriptor> models,
        ILogger<AnthropicModelProvider>? logger = null,
        AnthropicProviderOptions? healthCheckOptions = null,
        EgressSocketGuard? egressGuard = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(chatClientFactory);
        ArgumentNullException.ThrowIfNull(models);

        Name = name;
        Models = models;

        _chatClientFactory = chatClientFactory;
        _logger = logger;
        _healthCheck = healthCheckOptions is null ? null : new AnthropicProviderHealthCheck(name, healthCheckOptions, logger);
        _configurationDiagnostic = healthCheckOptions is null
            ? null
            : ModelProviderCore.ConfigurationDiagnosticFor(
                AnthropicProviderOptions.SectionName,
                resolved: !string.IsNullOrWhiteSpace(healthCheckOptions.ApiKey));
        _baseOptions = healthCheckOptions;
        _core = new ModelProviderCore<AnthropicChatClientFactory>(models, egressGuard);
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

        if (_core.IsOutsideCatalog(binding.Model))
        {
            ModelProviderCore.LogOutsideCatalog(
                _logger, binding.Model, Name, $"{AnthropicProviderOptions.SectionName}:Models");
        }

        return credential is null
            ? _chatClientFactory.CreateChatClient(binding)
            : _core.GetTenantChatClient(
                credential,
                binding,
                BuildCredentialFactory,
                static (factory, tenantBinding) => factory.CreateChatClient(tenantBinding));
    }

    /// <summary>Builds a per-tenant client factory from a resolved credential (BYOK).</summary>
    /// <remarks>See <c>OpenAIModelProvider.BuildCredentialFactory</c> for the endpoint fallback rationale.</remarks>
    private AnthropicChatClientFactory BuildCredentialFactory(ModelProviderCredential credential)
    {
        var options = new AnthropicProviderOptions
        {
            ApiKey = credential.ApiKey,
            DefaultModel = _baseOptions?.DefaultModel,
            DefaultMaxOutputTokens = _baseOptions?.DefaultMaxOutputTokens ?? 4096,
            MaxRetries = _baseOptions?.MaxRetries,
            Timeout = _baseOptions?.Timeout,
        };

        var overrideEndpoint = ModelProviderCore.TenantEndpoint(credential);

        options.Endpoint = overrideEndpoint ?? _baseOptions?.Endpoint;

        var client = AnthropicChatClientFactory.CreateClient(options, _core.GuardFor(overrideEndpoint));

        return AnthropicChatClientFactory.FromClient(client, options.DefaultModel, options.DefaultMaxOutputTokens);
    }

    /// <inheritdoc />
    public ValueTask<ModelProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
        => _healthCheck?.CheckHealthAsync(cancellationToken)
            ?? ValueTask.FromResult(ModelProviderCore.UnknownHealth(Name));

    /// <inheritdoc />
    public ConfigurationDiagnostic? GetConfigurationDiagnostic() => _configurationDiagnostic;
}
