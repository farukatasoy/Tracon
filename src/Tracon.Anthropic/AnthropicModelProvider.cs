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
internal sealed class AnthropicModelProvider : ITenantCredentialModelProvider, ILiveSetupModelProvider, IModelProviderHealthCheck, IModelProviderConfigurationDiagnostics, ISamplingSupport, IHostedToolSupport
{
    private readonly AnthropicChatClientFactory _chatClientFactory;
    private readonly ILogger<AnthropicModelProvider>? _logger;
    private readonly AnthropicProviderHealthCheck? _healthCheck;
    private readonly ConfigurationDiagnostic? _configurationDiagnostic;
    private readonly AnthropicProviderOptions? _baseOptions;

    // Set when the host named the configuration keys of the key and the address: the clients follow them.
    private readonly LiveSetup<AnthropicChatClientFactory>? _live;

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
    /// <param name="timeProvider">
    /// The clock a date in a <c>Retry-After</c> header of a tenant's own client is read against;
    /// <see cref="TimeProvider.System"/> when omitted.
    /// </param>
    /// <param name="liveSetup">
    /// The clients that follow the configuration keys the options name for the key and the address, or
    /// <see langword="null"/> when the key and the address are fixed for the life of the host.
    /// </param>
    /// <exception cref="ArgumentNullException">One of the required dependencies is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    public AnthropicModelProvider(
        string name,
        AnthropicChatClientFactory chatClientFactory,
        IReadOnlyList<ModelDescriptor> models,
        ILogger<AnthropicModelProvider>? logger = null,
        AnthropicProviderOptions? healthCheckOptions = null,
        EgressSocketGuard? egressGuard = null,
        TimeProvider? timeProvider = null,
        LiveSetup<AnthropicChatClientFactory>? liveSetup = null)
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
                ? new AnthropicProviderHealthCheck(name, healthCheckOptions, logger)
                : new AnthropicProviderHealthCheck(name, () => HealthTargetOf(liveSetup, healthCheckOptions), logger);
        _configurationDiagnostic = healthCheckOptions is null || liveSetup is not null
            ? null
            : ModelProviderCore.ConfigurationDiagnosticFor(
                AnthropicProviderOptions.SectionName,
                resolved: !string.IsNullOrWhiteSpace(healthCheckOptions.ApiKey));
        _baseOptions = healthCheckOptions;
        _core = new ModelProviderCore<AnthropicChatClientFactory>(models, egressGuard, timeProvider);
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
    /// When the host named a configuration key for the key, the keys the provider has sent: the one now in use first,
    /// then the ones it replaced, so the text of a call that began before a rotation is masked too.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> SetupApiKeys
        => _live is not null ? _live.Source.RecentKeys : _chatClientFactory.SetupApiKey is { } key ? [key] : [];

    /// <inheritdoc />
    bool ILiveSetupModelProvider.SetupKeysChange => _live is not null;

    /// <inheritdoc />
    /// <remarks>
    /// Measured from the Anthropic 13.8.0 chat client adapter (decompiled 2026-11-14; the request options are the ones measured on the wire on 2026-10-28): it
    /// sends the stop sequences, the tool mode and the parallel-tool-use flag, and maps the reasoning output
    /// to the thinking display. It has no seed and no penalties, and since SDK 13 it has no temperature,
    /// top-p or top-k either: <c>MessageCreateParams</c> has no member for them, so the adapter drops them.
    /// Naming them here would let a definition run without a field it sets, which Tracon refuses; they are
    /// left out, and a definition that sets one fails to compile with the provider's name.
    /// </remarks>
    public SamplingFields SupportedSamplingFields { get; } = SamplingFields.StopSequences
        | SamplingFields.ToolMode
        | SamplingFields.AllowMultipleToolCalls
        | SamplingFields.ReasoningOutput;

    /// <inheritdoc />
    /// <remarks>
    /// States no limit. The Messages API sends no seed, and it does not bound the NUMBER of stop sequences: it
    /// bounds their total length (measured with real calls on 2026-10-27, <c>claude-haiku-4-5-20251001</c>:
    /// <c>stop_sequences: total length: 0..2,500</c>, and 2,500 one-character entries pass where 2,501 and
    /// 100 entries of 40 characters do not). A count limit cannot state that, so a definition that exceeds it
    /// gets the provider's own <c>400</c>.
    /// </remarks>
    public SamplingLimits SamplingLimits => SamplingLimits.None;

    /// <inheritdoc />
    /// <remarks>
    /// Measured with a real call (2026-10-20, <c>claude-haiku-4-5-20251001</c>): the adapter maps the web
    /// search to the web search tool and the code interpreter to the code execution tool, and both run.
    /// It has no file search.
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
                _logger, binding.Model, Name, $"{AnthropicProviderOptions.SectionName}:Models");
        }

        if (credential is null && _live is { } live)
        {
            // The client is picked when the call starts: an agent, a fallback link, a judge or a summarizer holds this
            // one and follows a rotation without being built again.
            IChatClient Current() => live.ChatClient(
                live.Snapshot(),
                binding,
                static (factory, liveBinding) => factory.CreateChatClient(liveBinding));

            // The first client is made now, as a fixed key makes it at compile time: a model name or a provider setting
            // that cannot be used stops the compilation here and not the first call. The cache keeps it for that call.
            _ = Current();

            return new LiveSetupChatClient(Current);
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

    // What a tenant that names no address of its own is built from besides its key: the setup address, which moves with a
    // configuration key. A moved address replaces the tenant's client, so a tenant is never left on the old gateway.
    private string? TenantEnvironment(ModelProviderCredential credential)
        => ModelProviderCore.TenantEnvironment(credential, SetupEndpoint(), setupAddressChanges: _live is not null);

    // The address the setup key is sent to now: the one a configuration key names, or the one the host started with.
    private Uri? SetupEndpoint()
        => _live is null
            ? _baseOptions?.Endpoint
            : Uri.TryCreate(_live.Snapshot().Endpoint, UriKind.Absolute, out var endpoint) ? endpoint : null;

    private static AnthropicProviderHealthCheck.HealthTarget HealthTargetOf(
        LiveSetup<AnthropicChatClientFactory> live,
        AnthropicProviderOptions options)
    {
        // Key and address of one check come from one snapshot. The placeholder stands for "no key known"; it is not sent.
        var snapshot = live.Snapshot();
        var key = string.Equals(snapshot.ApiKey, SetupCredentialSource.KeylessPlaceholder, StringComparison.Ordinal)
            ? null
            : snapshot.ApiKey;

        return new AnthropicProviderHealthCheck.HealthTarget(
            key,
            Uri.TryCreate(snapshot.Endpoint, UriKind.Absolute, out var endpoint) ? endpoint : null,
            options.Timeout);
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

        options.Endpoint = overrideEndpoint ?? SetupEndpoint();

        var client = AnthropicChatClientFactory.CreateClient(options, _core.GuardFor(overrideEndpoint), _core.Clock);

        return AnthropicChatClientFactory.FromClient(client, options.DefaultModel, options.DefaultMaxOutputTokens);
    }

    /// <inheritdoc />
    public ValueTask<ModelProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
        => _healthCheck?.CheckHealthAsync(cancellationToken)
            ?? ValueTask.FromResult(ModelProviderCore.UnknownHealth(Name));

    /// <inheritdoc />
    public ConfigurationDiagnostic? GetConfigurationDiagnostic()
        => _live is { } live
            ? ModelProviderCore.ConfigurationDiagnosticFor(
                AnthropicProviderOptions.SectionName,
                resolved: !string.Equals(live.Snapshot().ApiKey, SetupCredentialSource.KeylessPlaceholder, StringComparison.Ordinal),
                keyName: _baseOptions?.ApiKeyConfigurationName)
            : _configurationDiagnostic;
}
