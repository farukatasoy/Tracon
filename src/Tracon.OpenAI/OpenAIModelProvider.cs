using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

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
/// catalog. When OpenAI publishes a new model, no new Tracon release is needed; if
/// the catalog is not empty, only an informational log entry is written. Because
/// Tracon carries no built-in model list, an empty catalog is
/// normal; in that case nothing is logged.
/// </para>
/// </remarks>
internal sealed class OpenAIModelProvider : ITenantCredentialModelProvider, ILiveSetupModelProvider, IModelProviderHealthCheck, IModelProviderConfigurationDiagnostics, ISamplingSupport, IHostedToolSupport, IResponseSchemaSupport
{
    // Chat Completions: "up to 4 sequences" (documented for the stop parameter).
    private static readonly SamplingLimits ChatCompletionsLimits = new() { MaxStopSequences = 4 };

    private readonly OpenAIChatClientFactory _chatClientFactory;
    private readonly ILogger<OpenAIModelProvider>? _logger;
    private readonly OpenAIProviderHealthCheck? _healthCheck;
    private readonly ConfigurationDiagnostic? _configurationDiagnostic;
    private readonly OpenAIProviderOptions? _baseOptions;

    // Set when the host named the configuration keys of the key and the address: the clients follow them.
    private readonly LiveSetup<OpenAIChatClientFactory>? _live;
    private readonly string? _configurationSectionKey;

    // Where the operator edits the catalog; named in the out-of-catalog log entry.
    private readonly string _modelsSetting;

    // Known-model set, BYOK caches and the tenant endpoint guard rule (shared source).
    private readonly ModelProviderCore<OpenAIChatClientFactory> _core;

    /// <summary>Initializes a new provider.</summary>
    /// <param name="name">
    /// The provider name. The <see cref="ModelBinding.Provider"/> of agent definitions
    /// matches this value.
    /// </param>
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
    /// <see langword="null"/> when the key and the address are fixed for the life of the host.
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
        string? configurationSectionKey = OpenAIProviderOptions.SectionName,
        EgressSocketGuard? egressGuard = null,
        TimeProvider? timeProvider = null,
        LiveSetup<OpenAIChatClientFactory>? liveSetup = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(chatClientFactory);
        ArgumentNullException.ThrowIfNull(models);

        Name = name;
        ApiSurface = apiSurface;
        Models = models;

        _chatClientFactory = chatClientFactory;
        _logger = logger;
        _live = liveSetup;
        _configurationSectionKey = configurationSectionKey;
        _healthCheck = healthCheckOptions is null
            ? null
            : liveSetup is null
                ? new OpenAIProviderHealthCheck(name, healthCheckOptions, logger)
                : new OpenAIProviderHealthCheck(name, () => HealthTargetOf(liveSetup, healthCheckOptions), logger);
        _configurationDiagnostic = healthCheckOptions is null || configurationSectionKey is null || liveSetup is not null
            ? null
            : ModelProviderCore.ConfigurationDiagnosticFor(
                configurationSectionKey,
                resolved: !string.IsNullOrWhiteSpace(healthCheckOptions.ApiKey));

        // UseOpenAICompatible() has no fixed section: its catalog is the Models
        // list of the options it was given in code.
        _modelsSetting = configurationSectionKey is null
            ? $"{nameof(OpenAIProviderOptions)}.{nameof(OpenAIProviderOptions.Models)}"
            : $"{configurationSectionKey}:{nameof(OpenAIProviderOptions.Models)}";
        _baseOptions = healthCheckOptions;
        _core = new ModelProviderCore<OpenAIChatClientFactory>(models, egressGuard, timeProvider);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>Gets the OpenAI API surface this provider uses.</summary>
    public OpenAIApiSurface ApiSurface { get; }

    /// <inheritdoc />
    public IReadOnlyList<ModelDescriptor> Models { get; }

    /// <inheritdoc />
    /// <remarks>
    /// The key the provider's client was built with, trimmed as it is sent. Empty when the provider was built from a
    /// ready-made client (no key is known).
    /// </remarks>
    /// <remarks>
    /// When the host named a configuration key for the key, the keys the provider has sent: the one now in use first, then
    /// the ones it replaced, so the text of a call that began before a rotation is masked too.
    /// </remarks>
    public IReadOnlyList<string> SetupApiKeys
        => _live is not null ? _live.Source.RecentKeys : _chatClientFactory.SetupApiKey is { } key ? [key] : [];

    /// <inheritdoc />
    bool ILiveSetupModelProvider.SetupKeysChange => _live is not null;

    /// <inheritdoc />
    /// <remarks>
    /// Measured from the Microsoft.Extensions.AI.OpenAI 10.11.0 adapters (2026-10-20): Chat
    /// Completions sends the seed, the stop sequences and both penalties and has no top-k or
    /// reasoning output; the Responses API sends the temperature and top-p (measured on the wire,
    /// 2026-10-28) but none of the other sampling fields, and is the one that carries the reasoning
    /// summary. Both send the tool mode and the parallel-tool-calls flag.
    /// </remarks>
    public SamplingFields SupportedSamplingFields => ApiSurface switch
    {
        OpenAIApiSurface.Responses => SamplingFields.Temperature
            | SamplingFields.TopP
            | SamplingFields.ToolMode
            | SamplingFields.AllowMultipleToolCalls
            | SamplingFields.ReasoningOutput,
        _ => SamplingFields.Temperature
            | SamplingFields.TopP
            | SamplingFields.Seed
            | SamplingFields.StopSequences
            | SamplingFields.FrequencyPenalty
            | SamplingFields.PresencePenalty
            | SamplingFields.ToolMode
            | SamplingFields.AllowMultipleToolCalls,
    };

    /// <inheritdoc />
    /// <remarks>
    /// Chat Completions accepts at most four stop sequences, as OpenAI documents for <c>stop</c>. The seed
    /// is a 64-bit integer on the wire, so no seed range is stated. The limit is stated for OpenAI's own
    /// service only: a compatible server or a gateway behind another address can accept more, and a limit
    /// that is too narrow would reject a definition that runs there (the same reason the strict-schema
    /// refusals are measured against <c>api.openai.com</c> alone). The Responses API sends no stop
    /// sequences, so it states nothing. Not provoked with a real <c>400</c>: every model of the account that
    /// measured the table rejects <c>stop</c> itself (<c>unsupported_parameter</c>, 2026-10-27), and that
    /// answer comes before any check of the count.
    /// </remarks>
    public SamplingLimits SamplingLimits => ApiSurface switch
    {
        OpenAIApiSurface.Responses => SamplingLimits.None,
        _ => IsOpenAIService ? ChatCompletionsLimits : SamplingLimits.None,
    };

    /// <inheritdoc />
    /// <remarks>
    /// Measured from the Microsoft.Extensions.AI.OpenAI 10.11.0 adapters (2026-10-20): the Responses
    /// API runs a web search, a code interpreter and a file search. Chat Completions maps only the web
    /// search, and Tracon does not report it (the surface answers <c>400</c> for a model that cannot
    /// search); it drops the code interpreter without an error, so the model answers as if it
    /// had none.
    /// </remarks>
    public HostedToolKinds SupportedHostedTools => ApiSurface switch
    {
        OpenAIApiSurface.Responses => HostedToolKinds.WebSearch
            | HostedToolKinds.CodeInterpreter
            | HostedToolKinds.FileSearch,
        _ => HostedToolKinds.None,
    };

    /// <inheritdoc />
    /// <remarks>
    /// Both API surfaces and every <c>UseOpenAICompatible()</c> registration send the schema through the same
    /// Microsoft.Extensions.AI client code, so the answer is the same for all of them. See
    /// <see cref="OpenAIResponseSchema"/>.
    /// </remarks>
    public ResponseSchemaShape DescribeResponseSchema(ModelBinding binding, JsonElement schema)
    {
        ArgumentNullException.ThrowIfNull(binding);

        return OpenAIResponseSchema.Describe(
            schema,
            ModelProviderSettings.ReadBoolean(binding, OpenAIProviderNames.StrictJsonSchemaSetting) is true,
            measuredService: IsOpenAIService);
    }

    // The strict-mode refusals were measured against OpenAI itself. A custom address is a
    // gateway or a compatible server, which can accept what OpenAI refuses.
    private bool IsOpenAIService
        => SetupEndpoint() is not { } endpoint
            || string.Equals(endpoint.Host, "api.openai.com", StringComparison.OrdinalIgnoreCase);

    // What a tenant that names no address of its own is built from besides its key: the setup address, which moves with a
    // configuration key. A moved address replaces the tenant's client, so a tenant is never left on the old gateway.
    private string? TenantEnvironment(ModelProviderCredential credential)
        => ModelProviderCore.TenantEnvironment(credential, SetupEndpoint(), setupAddressChanges: _live is not null);

    // The address the setup key is sent to now: the one a configuration key names, or the one the host started with.
    private Uri? SetupEndpoint()
        => _live is null
            ? _baseOptions?.Endpoint
            : Uri.TryCreate(_live.Snapshot().Endpoint, UriKind.Absolute, out var endpoint) ? endpoint : null;

    private static OpenAIProviderHealthCheck.HealthTarget HealthTargetOf(
        LiveSetup<OpenAIChatClientFactory> live,
        OpenAIProviderOptions options)
    {
        // Key and address of one check come from one snapshot. A compatible server that asks for no key gets no header.
        var snapshot = live.Snapshot();
        var key = string.Equals(snapshot.ApiKey, SetupCredentialSource.KeylessPlaceholder, StringComparison.Ordinal) && live.Source.Keyless
            ? null
            : snapshot.ApiKey;

        return new OpenAIProviderHealthCheck.HealthTarget(
            key,
            Uri.TryCreate(snapshot.Endpoint, UriKind.Absolute, out var endpoint) ? endpoint : null,
            options.Timeout);
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
            ModelProviderCore.LogOutsideCatalog(_logger, binding.Model, Name, _modelsSetting);
        }

        if (credential is null && _live is { } live)
        {
            // One client is built now, so that a setting the provider refuses stops the compile of the agent and not its
            // first call. The cache keeps it: the first call gets the same one.
            _ = SetupChatClient(live, binding);

            // The client is picked when the call starts: an agent, a fallback link, a judge or a summarizer holds this
            // one and follows a rotation without being built again.
            return new LiveSetupChatClient(() => SetupChatClient(live, binding));
        }

        return credential is null
            ? _chatClientFactory.CreateChatClient(binding, ApiSurface)
            : _core.GetTenantChatClient(
                credential,
                binding,
                BuildCredentialFactory,
                (factory, tenantBinding) => factory.CreateChatClient(tenantBinding, ApiSurface),
                TenantEnvironment(credential));
    }

    private IChatClient SetupChatClient(LiveSetup<OpenAIChatClientFactory> live, ModelBinding binding)
        => live.ChatClient(live.Snapshot(), binding, (factory, liveBinding) => factory.CreateChatClient(liveBinding, ApiSurface));

    /// <summary>Builds a per-tenant client factory from a resolved credential (BYOK).</summary>
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

        var overrideEndpoint = ModelProviderCore.TenantEndpoint(credential);

        options.Endpoint = overrideEndpoint ?? SetupEndpoint();

        var client = OpenAIChatClientFactory.CreateClient(options, _core.GuardFor(overrideEndpoint), _core.Clock);

        return OpenAIChatClientFactory.FromClient(client, options.DefaultModel);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The health check does not use <see cref="_chatClientFactory"/> — it makes a
    /// separate, lightweight HTTP GET. See <see cref="OpenAIProviderHealthCheck"/>.
    /// </remarks>
    public ValueTask<ModelProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
        => _healthCheck?.CheckHealthAsync(cancellationToken)
            ?? ValueTask.FromResult(ModelProviderCore.UnknownHealth(Name));

    /// <inheritdoc />
    public ConfigurationDiagnostic? GetConfigurationDiagnostic()
        => _live is { } live && _configurationSectionKey is { } section
            ? ModelProviderCore.ConfigurationDiagnosticFor(
                section,
                resolved: !string.Equals(live.Snapshot().ApiKey, SetupCredentialSource.KeylessPlaceholder, StringComparison.Ordinal),
                keyName: _baseOptions?.ApiKeyConfigurationName)
            : _configurationDiagnostic;
}
