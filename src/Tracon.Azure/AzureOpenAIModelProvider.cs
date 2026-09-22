using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>The <see cref="IModelProvider"/> implementation for Azure OpenAI.</summary>
/// <remarks>
/// <para>
/// The provider <strong>does not reject</strong> a deployment name absent from
/// the catalog. Deployment names are chosen by whoever sets up the Azure
/// resource, and Tracon's configuration is not expected to be updated when
/// a new deployment is opened; when the catalog is non-empty, only an
/// informational log entry is left.
/// </para>
/// <para>
/// Circuit-breaker and content-filter detection live <em>outside</em> this type,
/// at the <c>ModelProviderRegistry</c> level; this package gets both for free.
/// When Azure's own content filter cuts a response short,
/// <c>ContentFilterDetectingChatClient</c> records it as <c>content_filtered</c>
/// — there is no extra code in this package for it.
/// </para>
/// </remarks>
internal sealed class AzureOpenAIModelProvider : ITenantCredentialModelProvider, IModelProviderHealthCheck, IModelProviderConfigurationDiagnostics
{
    private readonly AzureOpenAIChatClientFactory _chatClientFactory;
    private readonly ILogger<AzureOpenAIModelProvider>? _logger;
    private readonly AzureOpenAIProviderHealthCheck? _healthCheck;
    private readonly ConfigurationDiagnostic? _configurationDiagnostic;
    private readonly AzureOpenAIProviderOptions? _baseOptions;

    // Known-model set, BYOK caches and the tenant endpoint guard rule (shared source).
    private readonly ModelProviderCore<AzureOpenAIChatClientFactory> _core;

    /// <summary>Creates a new provider.</summary>
    /// <param name="name">The provider name. <see cref="ModelBinding.Provider"/> in agent definitions matches this value.</param>
    /// <param name="chatClientFactory">The chat client factory.</param>
    /// <param name="models">The deployments this provider offers.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="healthCheckOptions">
    /// When given, <see cref="CheckHealthAsync"/> calls the
    /// <c>GET {endpoint}/openai/models</c> endpoint using this option's address
    /// and credential. When <see langword="null"/>, health status always returns
    /// <see cref="ModelProviderHealthStatus.Unknown"/>.
    /// </param>
    /// <param name="egressGuard">
    /// The outbound network guard. Attached only to a client built from a
    /// tenant-supplied endpoint override; when <see langword="null"/>, such an
    /// override is not guarded.
    /// </param>
    /// <exception cref="ArgumentNullException">One of the required dependencies is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    public AzureOpenAIModelProvider(
        string name,
        AzureOpenAIChatClientFactory chatClientFactory,
        IReadOnlyList<ModelDescriptor> models,
        ILogger<AzureOpenAIModelProvider>? logger = null,
        AzureOpenAIProviderOptions? healthCheckOptions = null,
        EgressSocketGuard? egressGuard = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(chatClientFactory);
        ArgumentNullException.ThrowIfNull(models);

        Name = name;
        Models = models;

        _chatClientFactory = chatClientFactory;
        _logger = logger;
        _healthCheck = healthCheckOptions is null ? null : new AzureOpenAIProviderHealthCheck(name, healthCheckOptions, logger);
        _configurationDiagnostic = healthCheckOptions is null
            ? null
            : ModelProviderCore.ConfigurationDiagnosticFor(
                AzureOpenAIProviderOptions.SectionName,
                resolved: !string.IsNullOrWhiteSpace(healthCheckOptions.ApiKey) || healthCheckOptions.CredentialFactory is not null,
                alternative: $"assign {nameof(AzureOpenAIProviderOptions)}.{nameof(AzureOpenAIProviderOptions.CredentialFactory)}");
        _baseOptions = healthCheckOptions;
        _core = new ModelProviderCore<AzureOpenAIChatClientFactory>(models, egressGuard);
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
            LogUnknownDeployment(binding.Model);
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
    /// <remarks>
    /// See <c>OpenAIModelProvider.BuildCredentialFactory</c> for the endpoint
    /// fallback rationale. Unlike the other three providers, a missing endpoint
    /// here is not optional: Azure has no single global address, so when
    /// neither the credential nor the base setup carries one,
    /// <see cref="AzureOpenAIChatClientFactory.CreateClient"/> throws its own
    /// clear <see cref="TraconException"/>.
    /// </remarks>
    private AzureOpenAIChatClientFactory BuildCredentialFactory(ModelProviderCredential credential)
    {
        var options = new AzureOpenAIProviderOptions
        {
            ApiKey = credential.ApiKey,
            DefaultDeployment = _baseOptions?.DefaultDeployment,
            Audience = _baseOptions?.Audience,
            Timeout = _baseOptions?.Timeout,
        };

        var overrideEndpoint = ModelProviderCore.TenantEndpoint(credential);

        options.Endpoint = overrideEndpoint ?? _baseOptions?.Endpoint;

        var client = AzureOpenAIChatClientFactory.CreateClient(options, _core.GuardFor(overrideEndpoint));

        return AzureOpenAIChatClientFactory.FromClient(client, options.DefaultDeployment);
    }

    /// <inheritdoc />
    public ValueTask<ModelProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
        => _healthCheck?.CheckHealthAsync(cancellationToken)
            ?? ValueTask.FromResult(ModelProviderCore.UnknownHealth(Name));

    /// <inheritdoc />
    public ConfigurationDiagnostic? GetConfigurationDiagnostic() => _configurationDiagnostic;

    private void LogUnknownDeployment(string deployment)
    {
        if (_logger is not null && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "The name '{Deployment}' is not in the '{Provider}' catalog; the request is being sent anyway. " +
                "On Azure, this field expects a DEPLOYMENT name, not a MODEL name. To add the definition " +
                "to the catalog, use the Tracon:Providers:AzureOpenAI:Models option.",
                deployment,
                Name);
        }
    }
}
