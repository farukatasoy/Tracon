using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

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
internal sealed class GoogleModelProvider : ITenantCredentialModelProvider, ILiveSetupModelProvider, IModelProviderHealthCheck, IModelProviderConfigurationDiagnostics, ISamplingSupport, IHostedToolSupport
{
    private readonly GoogleChatClientFactory _chatClientFactory;
    private readonly ILogger<GoogleModelProvider>? _logger;
    private readonly GoogleProviderHealthCheck? _healthCheck;
    private readonly ConfigurationDiagnostic? _configurationDiagnostic;
    private readonly GoogleProviderOptions? _baseOptions;

    // Set when the host named the configuration keys of the key and the address: the clients follow them.
    private readonly LiveSetup<GoogleChatClientFactory>? _live;

    // Known-model set, BYOK caches and the tenant endpoint guard rule (shared source).
    // 🚨 GoogleChatClientFactory is IDisposable (owns an HttpClient); entries in _core
    // are never disposed. A factory that a rotated key replaces is dropped and left to
    // the collector, because a call in flight may still use it: the bounded,
    // admin-controlled trade-off ProviderCredentialClientCache documents.
    private readonly ModelProviderCore<GoogleChatClientFactory> _core;

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
    /// <param name="liveSetup">
    /// The clients that follow the configuration keys the options name for the key and the address, or
    /// <see langword="null"/> when the key and the address are fixed for the life of the host.
    /// </param>
    /// <exception cref="ArgumentNullException">One of the required dependencies is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    public GoogleModelProvider(
        string name,
        GoogleChatClientFactory chatClientFactory,
        IReadOnlyList<ModelDescriptor> models,
        ILogger<GoogleModelProvider>? logger = null,
        GoogleProviderOptions? healthCheckOptions = null,
        EgressSocketGuard? egressGuard = null,
        LiveSetup<GoogleChatClientFactory>? liveSetup = null)
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
                ? new GoogleProviderHealthCheck(name, healthCheckOptions, logger)
                : new GoogleProviderHealthCheck(name, () => HealthTargetOf(liveSetup, healthCheckOptions), logger);
        _configurationDiagnostic = healthCheckOptions is null || liveSetup is not null
            ? null
            : ModelProviderCore.ConfigurationDiagnosticFor(
                GoogleProviderOptions.SectionName,
                resolved: !string.IsNullOrWhiteSpace(healthCheckOptions.ApiKey));
        _baseOptions = healthCheckOptions;
        _core = new ModelProviderCore<GoogleChatClientFactory>(models, egressGuard);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public IReadOnlyList<ModelDescriptor> Models { get; }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The key the provider's client was built with, trimmed as it is sent. Empty when the provider was built from a
    /// ready-made client (no key is known).
    /// </para>
    /// <para>
    /// When the host named a configuration key for the key, the keys the provider has sent: the one now in use first, then
    /// the ones it replaced, so the text of a call that began before a rotation is masked too.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> SetupApiKeys
        => _live is not null ? _live.Source.RecentKeys : _chatClientFactory.SetupApiKey is { } key ? [key] : [];

    /// <inheritdoc />
    bool ILiveSetupModelProvider.SetupKeysChange => _live is not null;

    /// <inheritdoc />
    /// <remarks>
    /// Measured from the Google.GenAI 2.1.0 chat client adapter (decompiled 2026-11-14; its source is identical to the one the 1.x line had and to the one measured on 2026-10-20): it sends every field
    /// but the parallel-tool-calls flag, which Gemini has no request member for. The reasoning
    /// output becomes <c>includeThoughts</c>.
    /// </remarks>
    public SamplingFields SupportedSamplingFields { get; } = SamplingFields.Temperature
        | SamplingFields.TopP
        | SamplingFields.Seed
        | SamplingFields.TopK
        | SamplingFields.StopSequences
        | SamplingFields.FrequencyPenalty
        | SamplingFields.PresencePenalty
        | SamplingFields.ToolMode
        | SamplingFields.ReasoningOutput;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The seed is a 32-bit integer. Measured on 2026-10-27: the Gemini API accepts <c>-2147483648</c> and
    /// <c>2147483647</c> and answers <c>3000000000</c> with <c>400</c> (<c>TYPE_INT32</c>), and the
    /// Google.GenAI 2.1.0 chat client adapter (identical to the one measured first) narrows the seed with an unchecked <c>(int)</c> cast, so
    /// <c>3000000000</c> would reach the model as <c>-1294967296</c> and nothing would say so.
    /// </para>
    /// <para>
    /// At most five stop sequences: the Gemini API answers six with <c>400</c> (<c>the number of
    /// stop_sequences must not exceed 5</c>, measured on 2026-10-27, <c>gemini-3.6-flash</c>), as its
    /// reference states.
    /// </para>
    /// </remarks>
    public SamplingLimits SamplingLimits { get; } = new()
    {
        MinSeed = int.MinValue,
        MaxSeed = int.MaxValue,
        MaxStopSequences = 5,
    };

    /// <inheritdoc />
    /// <remarks>
    /// Measured with a real call (2026-10-20, <c>gemini-3.6-flash</c>): the web search and the code
    /// execution run. The adapter maps a file search to Google's <c>Retrieval</c> tool, which needs a
    /// retrieval source Tracon has no way to name, so it is not reported.
    /// </remarks>
    public HostedToolKinds SupportedHostedTools => HostedToolKinds.WebSearch | HostedToolKinds.CodeInterpreter;

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
                _logger, binding.Model, Name, $"{GoogleProviderOptions.SectionName}:Models");
        }

        if (credential is null && _live is { } live)
        {
            // The first client is built now, so a setting the provider refuses stops the compilation of the agent as it
            // does without a live setup. The client the call uses is picked when the call starts: an agent, a fallback
            // link, a judge or a summarizer holds this one and follows a rotation without being built again.
            _ = SetupChatClient(live, binding);

            return new LiveSetupChatClient(() => SetupChatClient(live, binding));
        }

        return credential is null
            ? _chatClientFactory.CreateChatClient(binding)
            : _core.GetTenantChatClient(
                credential,
                binding,
                BuildCredentialFactory,
                static (factory, tenantBinding) => factory.CreateChatClient(tenantBinding),
                TenantEnvironment(credential));
    }

    private static IChatClient SetupChatClient(LiveSetup<GoogleChatClientFactory> live, ModelBinding binding)
        => live.ChatClient(live.Snapshot(), binding, static (factory, liveBinding) => factory.CreateChatClient(liveBinding));

    // What a tenant that names no address of its own is built from besides its key: the setup address, which moves with a
    // configuration key. A moved address replaces the tenant's client, so a tenant is never left on the old gateway.
    private string? TenantEnvironment(ModelProviderCredential credential)
        => ModelProviderCore.TenantEnvironment(credential, SetupEndpoint(), setupAddressChanges: _live is not null);

    // The address the setup key is sent to now: the one a configuration key names, or the one the host started with.
    private Uri? SetupEndpoint()
        => _live is null
            ? _baseOptions?.Endpoint
            : Uri.TryCreate(_live.Snapshot().Endpoint, UriKind.Absolute, out var endpoint) ? endpoint : null;

    private static GoogleProviderHealthCheck.HealthTarget HealthTargetOf(
        LiveSetup<GoogleChatClientFactory> live,
        GoogleProviderOptions options)
    {
        // Key and address of one check come from one snapshot.
        var snapshot = live.Snapshot();

        return new GoogleProviderHealthCheck.HealthTarget(
            string.Equals(snapshot.ApiKey, SetupCredentialSource.KeylessPlaceholder, StringComparison.Ordinal) ? null : snapshot.ApiKey,
            Uri.TryCreate(snapshot.Endpoint, UriKind.Absolute, out var endpoint) ? endpoint : null,
            options.ApiVersion,
            options.Timeout);
    }

    /// <summary>Returns the client factory for a tenant credential (BYOK), the one the tenant's chat clients are built from.</summary>
    /// <param name="credential">The tenant's resolved credential.</param>
    /// <returns>The same factory for the same credential.</returns>
    /// <remarks>
    /// Google speech shares it: a tenant has one SDK client per credential, whether it chats or speaks.
    /// </remarks>
    internal GoogleChatClientFactory GetTenantFactory(ModelProviderCredential credential)
        => _core.GetTenantFactory(credential, BuildCredentialFactory, TenantEnvironment(credential));

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

        var overrideEndpoint = ModelProviderCore.TenantEndpoint(credential);

        options.Endpoint = overrideEndpoint ?? SetupEndpoint();

        var client = GoogleChatClientFactory.CreateClient(options, _core.GuardFor(overrideEndpoint));

        return GoogleChatClientFactory.FromClient(client, options.DefaultModel);
    }

    /// <inheritdoc />
    public ValueTask<ModelProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
        => _healthCheck?.CheckHealthAsync(cancellationToken)
            ?? ValueTask.FromResult(ModelProviderCore.UnknownHealth(Name));

    /// <inheritdoc />
    public ConfigurationDiagnostic? GetConfigurationDiagnostic()
        => _live is { } live && _baseOptions is { } options
            ? ModelProviderCore.ConfigurationDiagnosticFor(
                GoogleProviderOptions.SectionName,
                resolved: !string.Equals(live.Snapshot().ApiKey, SetupCredentialSource.KeylessPlaceholder, StringComparison.Ordinal),
                keyName: options.ApiKeyConfigurationName)
            : _configurationDiagnostic;
}
