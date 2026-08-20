using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>The <see cref="IModelProvider"/> implementation for Azure OpenAI.</summary>
/// <remarks>
/// <para>
/// The provider <strong>does not reject</strong> a deployment name absent from
/// the catalog. Deployment names are chosen by whoever sets up the Azure
/// resource, and AgentPrism's configuration is not expected to be updated when
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
public sealed class AzureOpenAIModelProvider : IModelProvider, IModelProviderHealthCheck, IModelProviderConfigurationDiagnostics
{
    private readonly AzureOpenAIChatClientFactory _chatClientFactory;
    private readonly ILogger<AzureOpenAIModelProvider>? _logger;
    private readonly HashSet<string> _knownDeployments;
    private readonly AzureOpenAIProviderHealthCheck? _healthCheck;
    private readonly ConfigurationDiagnostic? _configurationDiagnostic;
    private readonly AzureOpenAIProviderOptions? _baseOptions;

    // Phase 65 (BYOK). See OpenAIModelProvider's remark on _credentialFactories.
    private readonly ProviderCredentialClientCache<AzureOpenAIChatClientFactory> _credentialFactories = new();

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
    /// <exception cref="ArgumentNullException">One of the required dependencies is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
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
        _configurationDiagnostic = BuildConfigurationDiagnostic(healthCheckOptions);
        _baseOptions = healthCheckOptions;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public IReadOnlyList<ModelDescriptor> Models { get; }

    /// <inheritdoc />
    public IChatClient CreateChatClient(ModelBinding binding, ModelProviderCredential? credential = null)
    {
        ArgumentNullException.ThrowIfNull(binding);

        // When the catalog is empty there is nothing to compare against; logging
        // on every call would be noise (decision K-032).
        if (_knownDeployments.Count > 0
            && !string.IsNullOrWhiteSpace(binding.Model)
            && !_knownDeployments.Contains(binding.Model))
        {
            LogUnknownDeployment(binding.Model);
        }

        var factory = credential is null
            ? _chatClientFactory
            : _credentialFactories.GetOrAdd(credential, BuildCredentialFactory);

        return factory.CreateChatClient(binding);
    }

    /// <summary>Builds a per-tenant client factory from a resolved credential (BYOK).</summary>
    /// <remarks>
    /// See <c>OpenAIModelProvider.BuildCredentialFactory</c> for the endpoint
    /// fallback rationale. Unlike the other three providers, a missing endpoint
    /// here is not optional: Azure has no single global address, so when
    /// neither the credential nor the base setup carries one,
    /// <see cref="AzureOpenAIChatClientFactory.CreateClient"/> throws its own
    /// clear <see cref="AgentPrismException"/>.
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

        options.Endpoint = credential.Endpoint is { Length: > 0 } endpoint && Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
            ? endpointUri
            : _baseOptions?.Endpoint;

        var client = AzureOpenAIChatClientFactory.CreateClient(options);

        return AzureOpenAIChatClientFactory.FromClient(client, options.DefaultDeployment);
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

    private static ConfigurationDiagnostic? BuildConfigurationDiagnostic(AzureOpenAIProviderOptions? options)
    {
        if (options is null)
        {
            return null;
        }

        var key = $"{AzureOpenAIProviderOptions.SectionName}:ApiKey";
        var resolved = !string.IsNullOrWhiteSpace(options.ApiKey) || options.CredentialFactory is not null;

        return new ConfigurationDiagnostic
        {
            Key = key,
            Resolved = resolved,
            Hint = resolved
                ? null
                : $"dotnet user-secrets set \"{key}\" \"<key>\" or assign AzureOpenAIProviderOptions.CredentialFactory",
        };
    }

    private void LogUnknownDeployment(string deployment)
    {
        if (_logger is not null && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "The name '{Deployment}' is not in the '{Provider}' catalog; the request is being sent anyway. " +
                "On Azure, this field expects a DEPLOYMENT name, not a MODEL name. To add the definition " +
                "to the catalog, use the AgentPrism:Providers:AzureOpenAI:Models option.",
                deployment,
                Name);
        }
    }
}
