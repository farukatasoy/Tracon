using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// The <see cref="IModelProvider"/> implementation for OpenAI.
/// </summary>
/// <remarks>
/// <para>
/// <c>UseOpenAI()</c> registers <strong>two</strong> instances of this type:
/// <see cref="OpenAIProviderNames.ChatCompletions"/> and
/// <see cref="OpenAIProviderNames.Responses"/>. Both share the same
/// <see cref="OpenAIChatClientFactory"/> instance, and therefore the same HTTP
/// connection pool; only their <see cref="ApiSurface"/> values differ.
/// </para>
/// <para>
/// The provider does <strong>not reject</strong> a model name that is absent from the
/// catalog. When OpenAI publishes a new model, no new AgentPrism release is needed; if
/// the catalog is not empty, only an informational log entry is written. Because
/// AgentPrism carries no built-in model list (decision K-032), an empty catalog is
/// normal; in that case nothing is logged.
/// </para>
/// </remarks>
public sealed class OpenAIModelProvider : IModelProvider, IModelProviderHealthCheck, IModelProviderConfigurationDiagnostics
{
    private readonly OpenAIChatClientFactory _chatClientFactory;
    private readonly ILogger<OpenAIModelProvider>? _logger;
    private readonly HashSet<string> _knownModels;
    private readonly OpenAIProviderHealthCheck? _healthCheck;
    private readonly ConfigurationDiagnostic? _configurationDiagnostic;
    private readonly OpenAIProviderOptions? _baseOptions;

    // Phase 65 (BYOK). Measured: OpenAIClient is built once and shared, so a
    // tenant credential cannot reuse it — a second client is built and cached
    // per distinct credential, the same pattern OpenAINamedChatClientFactoryCache
    // already uses for named OpenAI-compatible providers.
    private readonly ProviderCredentialClientCache<OpenAIChatClientFactory> _credentialFactories = new();

    /// <summary>Initializes a new provider.</summary>
    /// <param name="name">The provider name. The <see cref="ModelBinding.Provider"/> of agent definitions matches this value.</param>
    /// <param name="apiSurface">The OpenAI API surface to use.</param>
    /// <param name="chatClientFactory">The chat client factory.</param>
    /// <param name="models">The models this provider offers.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="healthCheckOptions">
    /// When given, <see cref="CheckHealthAsync"/> calls the <c>GET {endpoint}/models</c>
    /// endpoint with the address and key from these options. When <see langword="null"/>,
    /// the health status is always <see cref="ModelProviderHealthStatus.Unknown"/>.
    /// </param>
    /// <param name="configurationSectionKey">
    /// The fixed configuration section that <see cref="GetConfigurationDiagnostic"/>
    /// reports. The default is <see cref="OpenAIProviderOptions.SectionName"/> — it is
    /// meant for <c>UseOpenAI()</c>. <c>UseOpenAICompatible()</c> passes
    /// <see langword="null"/> because it takes the key freely in code (there is no fixed
    /// section path); in that case no <see cref="ConfigurationDiagnostic"/> is reported.
    /// </param>
    /// <exception cref="ArgumentNullException">A required dependency is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
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
        _baseOptions = healthCheckOptions;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>Gets the OpenAI API surface this provider uses.</summary>
    public OpenAIApiSurface ApiSurface { get; }

    /// <inheritdoc />
    public IReadOnlyList<ModelDescriptor> Models { get; }

    /// <inheritdoc />
    public IChatClient CreateChatClient(ModelBinding binding, ModelProviderCredential? credential = null)
    {
        ArgumentNullException.ThrowIfNull(binding);

        // When the catalog is empty there is nothing to compare against, and logging on
        // every call would be noise. AgentPrism carries no built-in model list
        // (decision K-032), so an empty catalog is the normal case.
        if (_knownModels.Count > 0
            && !string.IsNullOrWhiteSpace(binding.Model)
            && !_knownModels.Contains(binding.Model))
        {
            LogUnknownModel(binding.Model);
        }

        var factory = credential is null
            ? _chatClientFactory
            : _credentialFactories.GetOrAdd(credential, BuildCredentialFactory);

        return factory.CreateChatClient(binding, ApiSurface);
    }

    /// <summary>Builds a per-tenant client factory from a resolved credential (phase 65, BYOK).</summary>
    /// <remarks>
    /// The endpoint falls back to the setup-time endpoint when the credential
    /// carries none: a globally configured OpenAI-compatible base address
    /// (for example a proxy) should still apply even when a tenant overrides
    /// only the key. The key itself never falls back to the setup-time key.
    /// </remarks>
    private OpenAIChatClientFactory BuildCredentialFactory(ModelProviderCredential credential)
    {
        var options = new OpenAIProviderOptions
        {
            ApiKey = credential.ApiKey,
            DefaultModel = _baseOptions?.DefaultModel,
            Organization = _baseOptions?.Organization,
            Timeout = _baseOptions?.Timeout,
        };

        if (credential.Endpoint is { Length: > 0 } endpoint && Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri))
        {
            options.Endpoint = endpointUri;
        }
        else
        {
            options.Endpoint = _baseOptions?.Endpoint;
        }

        var client = OpenAIChatClientFactory.CreateClient(options);

        return OpenAIChatClientFactory.FromClient(client, options.DefaultModel);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The health check does not use <see cref="_chatClientFactory"/> — it makes a
    /// separate, lightweight HTTP GET. See <see cref="OpenAIProviderHealthCheck"/>.
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
            Hint = resolved ? null : $"dotnet user-secrets set \"{key}\" \"<key>\"",
        };
    }

    private void LogUnknownModel(string model)
    {
        if (_logger is not null && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Model '{Model}' is not in the '{Provider}' catalog; the request is sent anyway. " +
                "Use the AgentPrism:Providers:OpenAI:Models option to add the model to the catalog.",
                model,
                Name);
        }
    }
}
