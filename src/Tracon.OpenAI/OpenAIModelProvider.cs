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
internal sealed class OpenAIModelProvider : ITenantCredentialModelProvider, IModelProviderHealthCheck, IModelProviderConfigurationDiagnostics, ISamplingSupport, IHostedToolSupport, IResponseSchemaSupport
{
    // Chat Completions: "up to 4 sequences" (documented for the stop parameter).
    private static readonly SamplingLimits ChatCompletionsLimits = new() { MaxStopSequences = 4 };

    private readonly OpenAIChatClientFactory _chatClientFactory;
    private readonly ILogger<OpenAIModelProvider>? _logger;
    private readonly OpenAIProviderHealthCheck? _healthCheck;
    private readonly ConfigurationDiagnostic? _configurationDiagnostic;
    private readonly OpenAIProviderOptions? _baseOptions;

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
        EgressSocketGuard? egressGuard = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(chatClientFactory);
        ArgumentNullException.ThrowIfNull(models);

        Name = name;
        ApiSurface = apiSurface;
        Models = models;

        _chatClientFactory = chatClientFactory;
        _logger = logger;
        _healthCheck = healthCheckOptions is null ? null : new OpenAIProviderHealthCheck(name, healthCheckOptions, logger);
        _configurationDiagnostic = healthCheckOptions is null || configurationSectionKey is null
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
        _core = new ModelProviderCore<OpenAIChatClientFactory>(models, egressGuard);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>Gets the OpenAI API surface this provider uses.</summary>
    public OpenAIApiSurface ApiSurface { get; }

    /// <inheritdoc />
    public IReadOnlyList<ModelDescriptor> Models { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Measured from the Microsoft.Extensions.AI.OpenAI 10.11.0 adapters (2026-10-20): Chat
    /// Completions sends the seed, the stop sequences and both penalties and has no top-k or
    /// reasoning output; the Responses API sends none of the sampling fields and is the one that
    /// carries the reasoning summary. Both send the tool mode and the parallel-tool-calls flag.
    /// </remarks>
    public SamplingFields SupportedSamplingFields => ApiSurface switch
    {
        OpenAIApiSurface.Responses => SamplingFields.ToolMode
            | SamplingFields.AllowMultipleToolCalls
            | SamplingFields.ReasoningOutput,
        _ => SamplingFields.Seed
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

    // The strict-mode refusals were measured against OpenAI itself (Phase 245). A custom address is a
    // gateway or a compatible server, which can accept what OpenAI refuses.
    private bool IsOpenAIService
        => _baseOptions?.Endpoint is not { } endpoint
            || string.Equals(endpoint.Host, "api.openai.com", StringComparison.OrdinalIgnoreCase);

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

        return credential is null
            ? _chatClientFactory.CreateChatClient(binding, ApiSurface)
            : _core.GetTenantChatClient(
                credential,
                binding,
                BuildCredentialFactory,
                (factory, tenantBinding) => factory.CreateChatClient(tenantBinding, ApiSurface));
    }

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

        options.Endpoint = overrideEndpoint ?? _baseOptions?.Endpoint;

        var client = OpenAIChatClientFactory.CreateClient(options, _core.GuardFor(overrideEndpoint));

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
    public ConfigurationDiagnostic? GetConfigurationDiagnostic() => _configurationDiagnostic;
}
