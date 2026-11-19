using System.Text.Json;
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
/// <c>FinishReasonDetectingChatClient</c> fails the run as <c>content_filtered</c> if the
/// final turn of the response is empty and records a <c>tracon.output.filtered</c> event if it
/// already has text — there is no extra code in this package for it.
/// </para>
/// </remarks>
internal sealed class AzureOpenAIModelProvider : ITenantCredentialModelProvider, ILiveSetupModelProvider, IModelProviderHealthCheck, IModelProviderConfigurationDiagnostics, ISamplingSupport, IHostedToolSupport, IResponseSchemaSupport
{
    private readonly AzureOpenAIChatClientFactory _chatClientFactory;
    private readonly ILogger<AzureOpenAIModelProvider>? _logger;
    private readonly AzureOpenAIProviderHealthCheck? _healthCheck;
    private readonly ConfigurationDiagnostic? _configurationDiagnostic;
    private readonly AzureOpenAIProviderOptions? _baseOptions;

    // Known-model set, BYOK caches and the tenant endpoint guard rule (shared source).
    private readonly ModelProviderCore<AzureOpenAIChatClientFactory> _core;

    // Set when the host named the configuration keys of the key and the address: the clients follow them.
    private readonly LiveSetup<AzureOpenAIChatClientFactory>? _live;

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
    /// <param name="timeProvider">
    /// The clock the retry wait of a tenant's own client runs on; <see cref="TimeProvider.System"/> when omitted.
    /// </param>
    /// <param name="liveSetup">
    /// The clients that follow the configuration keys the options name for the key and the address, or
    /// <see langword="null"/> when they are fixed for the life of the host.
    /// </param>
    /// <exception cref="ArgumentNullException">One of the required dependencies is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    public AzureOpenAIModelProvider(
        string name,
        AzureOpenAIChatClientFactory chatClientFactory,
        IReadOnlyList<ModelDescriptor> models,
        ILogger<AzureOpenAIModelProvider>? logger = null,
        AzureOpenAIProviderOptions? healthCheckOptions = null,
        EgressSocketGuard? egressGuard = null,
        TimeProvider? timeProvider = null,
        LiveSetup<AzureOpenAIChatClientFactory>? liveSetup = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(chatClientFactory);
        ArgumentNullException.ThrowIfNull(models);

        Name = name;
        Models = models;

        _chatClientFactory = chatClientFactory;
        _logger = logger;
        _live = liveSetup;
        _healthCheck = healthCheckOptions is null
            ? null
            : liveSetup is null
                ? new AzureOpenAIProviderHealthCheck(name, healthCheckOptions, logger)
                : new AzureOpenAIProviderHealthCheck(name, healthCheckOptions, () => HealthOptionsOf(liveSetup, healthCheckOptions), logger);
        _configurationDiagnostic = healthCheckOptions is null || liveSetup is not null
            ? null
            : ModelProviderCore.ConfigurationDiagnosticFor(
                AzureOpenAIProviderOptions.SectionName,
                resolved: !string.IsNullOrWhiteSpace(healthCheckOptions.ApiKey) || healthCheckOptions.CredentialFactory is not null,
                alternative: $"assign {nameof(AzureOpenAIProviderOptions)}.{nameof(AzureOpenAIProviderOptions.CredentialFactory)}");
        _baseOptions = healthCheckOptions;
        _core = new ModelProviderCore<AzureOpenAIChatClientFactory>(models, egressGuard, timeProvider);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public IReadOnlyList<ModelDescriptor> Models { get; }

    /// <inheritdoc />
    /// <remarks>
    /// The key the provider's client was built with, trimmed as it is sent. Empty when the provider was built from a
    /// ready-made client (no key is known). Empty when the provider was set up with a credential factory: a managed credential is not a key.
    /// </remarks>
    public IReadOnlyList<string> SetupApiKeys
        => _live is not null
            ? _baseOptions?.CredentialFactory is not null ? [] : _live.Source.RecentKeys
            : _chatClientFactory.SetupApiKey is { } key ? [key] : [];

    /// <inheritdoc />
    bool ILiveSetupModelProvider.SetupKeysChange => _live is not null && _baseOptions?.CredentialFactory is null;

    // The options of one health check: the key and the address of the current snapshot, the rest as set up.
    private static AzureOpenAIProviderOptions HealthOptionsOf(
        LiveSetup<AzureOpenAIChatClientFactory> live,
        AzureOpenAIProviderOptions options)
    {
        var snapshot = live.Snapshot();

        return new AzureOpenAIProviderOptions
        {
            Endpoint = Uri.TryCreate(snapshot.Endpoint, UriKind.Absolute, out var endpoint) ? endpoint : null,
            ApiKey = options.CredentialFactory is null ? snapshot.ApiKey : null,
            CredentialFactory = options.CredentialFactory,
            Audience = options.Audience,
            Timeout = options.Timeout,
        };
    }

    // The address the setup key is sent to now: the one a configuration key names, or the one the host started with.
    private Uri? SetupEndpoint()
        => _live is null
            ? _baseOptions?.Endpoint
            : Uri.TryCreate(_live.Snapshot().Endpoint, UriKind.Absolute, out var endpoint) ? endpoint : null;

    /// <inheritdoc />
    /// <remarks>
    /// The provider builds the Chat Completions client of the OpenAI adapter, so it sends what the
    /// <c>openai</c> provider's Chat Completions surface sends (measured 2026-10-20): the seed, the
    /// stop sequences, both penalties, the tool mode and the parallel-tool-calls flag.
    /// </remarks>
    public SamplingFields SupportedSamplingFields { get; } = SamplingFields.Temperature
        | SamplingFields.TopP
        | SamplingFields.Seed
        | SamplingFields.StopSequences
        | SamplingFields.FrequencyPenalty
        | SamplingFields.PresencePenalty
        | SamplingFields.ToolMode
        | SamplingFields.AllowMultipleToolCalls;

    /// <inheritdoc />
    /// <remarks>
    /// Azure OpenAI documents at most four stop sequences for <c>stop</c>, as OpenAI does for the same
    /// Chat Completions request. The seed is a 64-bit integer on the wire, so no seed range is stated. Not
    /// provoked with a real <c>400</c> (no Azure resource was available).
    /// </remarks>
    public SamplingLimits SamplingLimits { get; } = new() { MaxStopSequences = 4 };

    /// <inheritdoc />
    /// <remarks>
    /// The provider builds the Chat Completions client, which runs no hosted tool Tracon reports (see
    /// <c>OpenAIModelProvider</c>).
    /// </remarks>
    public HostedToolKinds SupportedHostedTools => HostedToolKinds.None;

    /// <inheritdoc />
    /// <remarks>
    /// The provider builds the same Microsoft.Extensions.AI Chat Completions client as <c>OpenAIModelProvider</c>,
    /// so the schema is rewritten the same way. See <see cref="OpenAIResponseSchema"/>.
    /// </remarks>
    public ResponseSchemaShape DescribeResponseSchema(ModelBinding binding, JsonElement schema)
    {
        ArgumentNullException.ThrowIfNull(binding);

        return OpenAIResponseSchema.Describe(
            schema,
            ModelProviderSettings.ReadBoolean(binding, AzureOpenAIProviderNames.StrictJsonSchemaSetting) is true);
    }

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

        if (credential is null && _live is { } live)
        {
            // One client is built now, so that a setting the provider refuses stops the compile of the agent and not its
            // first call. The cache keeps it: the first call gets the same one.
            _ = SetupChatClient(live, binding);

            // The client is picked when the call starts: an agent, a fallback link, a judge or a summarizer holds this one
            // and follows a rotation without being built again.
            return new LiveSetupChatClient(() => SetupChatClient(live, binding));
        }

        return credential is null
            ? _chatClientFactory.CreateChatClient(binding)
            : _core.GetTenantChatClient(
                credential,
                binding,
                BuildCredentialFactory,
                static (factory, tenantBinding) => factory.CreateChatClient(tenantBinding));
    }

    private static IChatClient SetupChatClient(LiveSetup<AzureOpenAIChatClientFactory> live, ModelBinding binding)
        => live.ChatClient(live.Snapshot(), binding, static (factory, liveBinding) => factory.CreateChatClient(liveBinding));

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

        options.Endpoint = overrideEndpoint ?? SetupEndpoint();

        var client = AzureOpenAIChatClientFactory.CreateClient(options, _core.GuardFor(overrideEndpoint), timeProvider: _core.Clock);

        return AzureOpenAIChatClientFactory.FromClient(client, options.DefaultDeployment);
    }

    /// <inheritdoc />
    public ValueTask<ModelProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
        => _healthCheck?.CheckHealthAsync(cancellationToken)
            ?? ValueTask.FromResult(ModelProviderCore.UnknownHealth(Name));

    /// <inheritdoc />
    public ConfigurationDiagnostic? GetConfigurationDiagnostic()
        => _live is { } live
            ? ModelProviderCore.ConfigurationDiagnosticFor(
                AzureOpenAIProviderOptions.SectionName,
                resolved: _baseOptions?.CredentialFactory is not null
                    || !string.Equals(live.Snapshot().ApiKey, SetupCredentialSource.KeylessPlaceholder, StringComparison.Ordinal),
                alternative: $"assign {nameof(AzureOpenAIProviderOptions)}.{nameof(AzureOpenAIProviderOptions.CredentialFactory)}",
                keyName: _baseOptions?.ApiKeyConfigurationName)
            : _configurationDiagnostic;

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
