using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Registry that holds the registered <see cref="IModelProvider"/>
/// implementations by name, and the single place that assembles the model-call pipeline.
/// </summary>
/// <remarks>
/// <para>
/// In Phase 1 no provider is registered; the <c>AgentPrism.OpenAI</c> package
/// (Phase 3) adds the first provider with <c>UseOpenAI()</c>. Trying to compile
/// while no provider is registered produces a clear error.
/// </para>
/// <para>
/// 🚨 <strong>The entire pipeline is assembled here</strong> (moved in Phase 48).
/// Previously the four provider packages each assembled the
/// <c>UseFunctionInvocation()</c> + <c>UseOpenTelemetry()</c> chain
/// <em>inside themselves</em>; every ring the registry wrapped stayed OUTSIDE
/// that chain, making it impossible to write a ring that sees the tool-call
/// turns. Now <see cref="IModelProvider"/> returns the <strong>raw</strong>
/// client and the pipeline is assembled in a single place; a third-party
/// provider inherits every ring for free too.
/// </para>
/// </remarks>
public sealed class ModelProviderRegistry : IModelProviderRegistry
{
    private readonly Dictionary<string, IModelProvider> _providers;
    private readonly ModelProviderCircuitBreaker? _circuitBreaker;
    private readonly IAttachmentStore? _attachmentStore;
    private readonly ITenantContext? _tenantContext;
    private readonly ContentGuardPipeline? _contentGuards;
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>Creates a new registry from the registered providers.</summary>
    /// <param name="providers">The model providers.</param>
    /// <param name="circuitBreaker">
    /// The circuit breaker that wraps the produced clients. If <see langword="null"/>,
    /// no wrapping is done (for example, in directly constructed tests).
    /// </param>
    /// <param name="attachmentStore">
    /// The store used to resolve attachment references. If not given together
    /// with <paramref name="tenantContext"/>, no attachment-resolving wrapper is added.
    /// </param>
    /// <param name="tenantContext">The tenant context used for attachment resolution.</param>
    /// <param name="contentGuards">
    /// The content inspection pipeline. If <see langword="null"/> or no
    /// <see cref="IContentGuard"/> is registered, the inspection wrapper is
    /// <strong>never added</strong>.
    /// </param>
    /// <param name="loggerFactory">
    /// The logger factory for <c>UseFunctionInvocation()</c> and
    /// <c>UseOpenTelemetry()</c>. If <see langword="null"/>, MAF uses its own default.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="providers"/> is <see langword="null"/>.</exception>
    /// <exception cref="AgentPrismException">The same provider name has been registered more than once.</exception>
    public ModelProviderRegistry(
        IEnumerable<IModelProvider> providers,
        ModelProviderCircuitBreaker? circuitBreaker = null,
        IAttachmentStore? attachmentStore = null,
        ITenantContext? tenantContext = null,
        ContentGuardPipeline? contentGuards = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _providers = new Dictionary<string, IModelProvider>(StringComparer.OrdinalIgnoreCase);
        _circuitBreaker = circuitBreaker;
        _attachmentStore = attachmentStore;
        _tenantContext = tenantContext;
        _contentGuards = contentGuards;
        _loggerFactory = loggerFactory;

        foreach (var provider in providers)
        {
            if (!_providers.TryAdd(provider.Name, provider))
            {
                throw new AgentPrismException(
                    $"More than one model provider named '{provider.Name}' has been registered.");
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ModelProviderDescriptor> List()
    {
        var descriptors = new List<ModelProviderDescriptor>(_providers.Count);

        foreach (var provider in _providers.Values)
        {
            descriptors.Add(new ModelProviderDescriptor
            {
                Name = provider.Name,
                Models = provider.Models,
            });
        }

        descriptors.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
        return descriptors;
    }

    /// <inheritdoc />
    public IChatClient CreateChatClient(ModelBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (!_providers.TryGetValue(binding.Provider, out var provider))
        {
            var known = _providers.Count == 0
                ? "no provider is registered"
                : string.Join(", ", _providers.Keys);

            throw new AgentPrismException(
                $"No model provider named '{binding.Provider}' is registered. Registered providers: {known}. " +
                "For OpenAI, call `builder.AddAgentPrism().UseOpenAI(apiKey)`.");
        }

        // The provider returns the RAW client; the entire pipeline is assembled here.
        IChatClient chatClient = provider.CreateChatClient(binding);

        // 🚨 The content guard sits right above the real client, INSIDE the
        // tool-call loop. This way a blocked request never reaches the network
        // and — more importantly — every turn of the loop is inspected: a tool
        // result enters the model on the second call, and that is the most
        // common path for prompt injection. If no guard is registered this line
        // does nothing: no wrapper is added and not even a single 'if' runs on
        // the model path.
        if (_contentGuards is { HasGuards: true })
        {
            chatClient = new ContentGuardingChatClient(_contentGuards, binding.Model, chatClient);
        }

        // The tool-call loop and telemetry. Both used to live inside the
        // provider packages until Phase 48; they were moved out so a ring could
        // be placed INSIDE the loop. Order: the loop is outermost, telemetry is
        // inside it, so every real model call gets its own 'chat' span.
        chatClient = chatClient
            .AsBuilder()
            .UseFunctionInvocation(_loggerFactory)
            .UseOpenTelemetry(_loggerFactory, AgentPrismDiagnostics.ActivitySourceName)
            .Build();

        // The attachment-resolving wrapper sits OUTSIDE the loop: attachments
        // are only found on the turn's first user message, and re-resolving on
        // every tool turn would mean reading the same bytes from the store over and over.
        if (_attachmentStore is not null && _tenantContext is not null)
        {
            chatClient = new AttachmentResolvingChatClient(chatClient, _attachmentStore, _tenantContext);
        }

        if (_circuitBreaker is not null)
        {
            chatClient = _circuitBreaker.Wrap(binding.Provider, chatClient);
        }

        // Content-filter detection sits OUTERMOST — outside the circuit breaker.
        // A safety filter means the provider is healthy; if it were inside, the
        // exception it throws would increase the consecutive-failure counter
        // and a handful of content-filtered requests in a row would close the
        // circuit on the provider.
        return new ContentFilterDetectingChatClient(binding.Provider, chatClient);
    }
}
