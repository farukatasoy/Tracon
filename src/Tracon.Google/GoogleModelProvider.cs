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
internal sealed class GoogleModelProvider : ITenantCredentialModelProvider, IModelProviderHealthCheck, IModelProviderConfigurationDiagnostics, ISamplingSupport, IHostedToolSupport
{
    private readonly GoogleChatClientFactory _chatClientFactory;
    private readonly ILogger<GoogleModelProvider>? _logger;
    private readonly GoogleProviderHealthCheck? _healthCheck;
    private readonly ConfigurationDiagnostic? _configurationDiagnostic;
    private readonly GoogleProviderOptions? _baseOptions;

    // Known-model set, BYOK caches and the tenant endpoint guard rule (shared source).
    // 🚨 GoogleChatClientFactory is IDisposable (owns an HttpClient); cached
    // entries in _core are never disposed, the same bounded, admin-controlled
    // trade-off ProviderCredentialClientCache documents.
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
        _healthCheck = healthCheckOptions is null ? null : new GoogleProviderHealthCheck(name, healthCheckOptions, logger);
        _configurationDiagnostic = healthCheckOptions is null
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
    /// Measured from the Google.GenAI 1.26.1 chat client adapter (2026-10-20): it sends every field
    /// but the parallel-tool-calls flag, which Gemini has no request member for. The reasoning
    /// output becomes <c>includeThoughts</c>.
    /// </remarks>
    public SamplingFields SupportedSamplingFields { get; } = SamplingFields.Seed
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
    /// Google.GenAI 1.26.1 chat client adapter narrows the seed with an unchecked <c>(int)</c> cast, so
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

        return credential is null
            ? _chatClientFactory.CreateChatClient(binding)
            : _core.GetTenantChatClient(
                credential,
                binding,
                BuildCredentialFactory,
                static (factory, tenantBinding) => factory.CreateChatClient(tenantBinding));
    }

    /// <summary>Returns the client factory for a tenant credential (BYOK), the one the tenant's chat clients are built from.</summary>
    /// <param name="credential">The tenant's resolved credential.</param>
    /// <returns>The same factory for the same credential.</returns>
    /// <remarks>
    /// Google speech shares it: a tenant has one SDK client per credential, whether it chats or speaks.
    /// </remarks>
    internal GoogleChatClientFactory GetTenantFactory(ModelProviderCredential credential)
        => _core.GetTenantFactory(credential, BuildCredentialFactory);

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

        options.Endpoint = overrideEndpoint ?? _baseOptions?.Endpoint;

        var client = GoogleChatClientFactory.CreateClient(options, _core.GuardFor(overrideEndpoint));

        return GoogleChatClientFactory.FromClient(client, options.DefaultModel);
    }

    /// <inheritdoc />
    public ValueTask<ModelProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
        => _healthCheck?.CheckHealthAsync(cancellationToken)
            ?? ValueTask.FromResult(ModelProviderCore.UnknownHealth(Name));

    /// <inheritdoc />
    public ConfigurationDiagnostic? GetConfigurationDiagnostic() => _configurationDiagnostic;
}
