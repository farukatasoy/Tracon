using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Registry that holds the registered <see cref="IModelProvider"/>
/// implementations by name, and the single place that assembles the model-call pipeline.
/// </summary>
/// <remarks>
/// <para>
/// By default no provider is registered; the <c>AgentPrism.OpenAI</c> package
///  adds the first provider with <c>UseOpenAI()</c>. Trying to compile
/// while no provider is registered produces a clear error.
/// </para>
/// <para>
/// <strong>The entire pipeline is assembled here</strong>.
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
    private readonly ProviderConcurrencyLimiter? _concurrencyLimiter;
    private readonly ITenantProviderBindingStore? _tenantProviderBindings;
    private readonly ITenantEgressPolicyStore? _tenantEgressPolicies;
    private readonly TenantProviderCredentialResolver? _credentialResolver;

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
    /// <param name="concurrencyLimiter">
    /// The per-provider outgoing concurrency limiter. If
    /// <see langword="null"/>, no limiting wrapper is added.
    /// </param>
    /// <param name="tenantProviderBindings">
    /// The per-tenant provider binding store (BYOK). If
    /// <see langword="null"/>, or if <paramref name="tenantContext"/> is
    /// <see langword="null"/>, <see cref="CreateChatClientAsync"/> behaves
    /// exactly like <see cref="CreateChatClient"/>.
    /// </param>
    /// <param name="tenantEgressPolicies">
    /// The per-tenant egress policy store. If
    /// <see langword="null"/>, no tenant is restricted.
    /// </param>
    /// <param name="credentialResolver">
    /// Resolves a <see cref="TenantProviderBinding"/> into a
    /// <see cref="ModelProviderCredential"/>. Required together with
    /// <paramref name="tenantProviderBindings"/> for BYOK to take effect.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="providers"/> is <see langword="null"/>.</exception>
    /// <exception cref="AgentPrismException">The same provider name has been registered more than once.</exception>
    public ModelProviderRegistry(
        IEnumerable<IModelProvider> providers,
        ModelProviderCircuitBreaker? circuitBreaker = null,
        IAttachmentStore? attachmentStore = null,
        ITenantContext? tenantContext = null,
        ContentGuardPipeline? contentGuards = null,
        ILoggerFactory? loggerFactory = null,
        ProviderConcurrencyLimiter? concurrencyLimiter = null,
        ITenantProviderBindingStore? tenantProviderBindings = null,
        ITenantEgressPolicyStore? tenantEgressPolicies = null,
        TenantProviderCredentialResolver? credentialResolver = null)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _providers = new Dictionary<string, IModelProvider>(StringComparer.OrdinalIgnoreCase);
        _circuitBreaker = circuitBreaker;
        _attachmentStore = attachmentStore;
        _tenantContext = tenantContext;
        _contentGuards = contentGuards;
        _loggerFactory = loggerFactory;
        _concurrencyLimiter = concurrencyLimiter;
        _tenantProviderBindings = tenantProviderBindings;
        _tenantEgressPolicies = tenantEgressPolicies;
        _credentialResolver = credentialResolver;

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
        => BuildPipeline(binding, ResolveProvider(binding), credential: null, resolveFallback: SyncFallbackResolver);

    /// <inheritdoc />
    public async ValueTask<IChatClient> CreateChatClientAsync(ModelBinding binding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var provider = ResolveProvider(binding);
        var credential = await ResolveTenantCredentialAsync(binding, cancellationToken).ConfigureAwait(false);

        // 🚨 The resolver a fallback link uses to build ITS OWN client matters
        // (independent audit, phase 65): a fallback binding carries its own
        // Provider and must go through the SAME tenant credential/egress
        // resolution as the primary, not the sync/global-only path — otherwise
        // a fallback would silently use the global credential and bypass the
        // tenant's egress policy. CreateChatClientAsync is passed here, so a
        // triggered fallback recurses back into this exact method.
        return BuildPipeline(binding, provider, credential, resolveFallback: CreateChatClientAsync);
    }

    private ValueTask<IChatClient> SyncFallbackResolver(ModelBinding binding, CancellationToken cancellationToken)
        => new(CreateChatClient(binding));

    /// <inheritdoc />
    public async ValueTask<bool> HasTenantProviderOverrideAsync(ModelBinding binding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (_tenantContext is null || _tenantProviderBindings is null)
        {
            return false;
        }

        var tenantId = _tenantContext.TenantId;

        if (await _tenantProviderBindings.GetAsync(tenantId, binding.Provider, cancellationToken).ConfigureAwait(false) is not null)
        {
            return true;
        }

        foreach (var fallback in binding.Fallbacks)
        {
            if (await _tenantProviderBindings.GetAsync(tenantId, fallback.Provider, cancellationToken).ConfigureAwait(false) is not null)
            {
                return true;
            }
        }

        return false;
    }

    private IModelProvider ResolveProvider(ModelBinding binding)
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

        return provider;
    }

    /// <summary>
    /// Resolution order: (0) the tenant's egress policy must
    /// allow the provider, checked BEFORE any credential lookup — looking up
    /// a key for a forbidden provider is a path that should not run at all;
    /// (1) the tenant's own provider binding; (2) <see langword="null"/>
    /// (the setup-time global credential is used).
    /// </summary>
    private async ValueTask<ModelProviderCredential?> ResolveTenantCredentialAsync(
        ModelBinding binding,
        CancellationToken cancellationToken)
    {
        if (_tenantContext is null)
        {
            return null;
        }

        var tenantId = _tenantContext.TenantId;

        if (_tenantEgressPolicies is not null)
        {
            var policy = await _tenantEgressPolicies.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);

            if (policy is not null)
            {
                // 🚨 Every link of the fallback chain is checked here too, not
                // only the primary (independent audit, phase 65): a fallback
                // is a first-class ModelBinding.Provider in its own right, and
                // leaving it unchecked would let an agent definition name a
                // forbidden provider as a fallback and never be rejected at
                // compile time — only lazily, the first time the primary
                // actually failed over to it.
                if (!policy.AllowedProviders.Contains(binding.Provider, StringComparer.OrdinalIgnoreCase))
                {
                    throw new AgentPrismException(
                        $"Tenant '{tenantId}' is not allowed to call model provider '{binding.Provider}'. " +
                        $"Allowed providers: {string.Join(", ", policy.AllowedProviders)}.");
                }

                foreach (var fallback in binding.Fallbacks)
                {
                    if (!policy.AllowedProviders.Contains(fallback.Provider, StringComparer.OrdinalIgnoreCase))
                    {
                        throw new AgentPrismException(
                            $"Tenant '{tenantId}' is not allowed to call model provider '{fallback.Provider}' " +
                            $"(used as a fallback for '{binding.Provider}'). " +
                            $"Allowed providers: {string.Join(", ", policy.AllowedProviders)}.");
                    }
                }
            }
        }

        if (_tenantProviderBindings is null || _credentialResolver is null)
        {
            return null;
        }

        var tenantBinding = await _tenantProviderBindings.GetAsync(tenantId, binding.Provider, cancellationToken).ConfigureAwait(false);

        if (tenantBinding is null)
        {
            // No binding for this tenant/provider: fall back to the global
            // setup-time credential (K1 — zero surprise when BYOK is not configured).
            return null;
        }

        // 🚨 A binding EXISTS but resolves to no value (the configuration key was
        // never set): this must NOT fall back to the global key silently — that
        // would bill the wrong tenant. Section 65.4.
        return _credentialResolver.Resolve(tenantBinding)
            ?? throw new AgentPrismException(
                $"Tenant '{tenantId}' has a provider binding for '{binding.Provider}' pointing at configuration key " +
                $"'{tenantBinding.ApiKeyConfigurationName}', but that key has no value. Set it with " +
                $"`dotnet user-secrets set \"{tenantBinding.ApiKeyConfigurationName}\" \"<key>\"` " +
                "or through your configuration provider — the call does NOT fall back to the global key.");
    }

    private ContentFilterDetectingChatClient BuildPipeline(
        ModelBinding binding,
        IModelProvider provider,
        ModelProviderCredential? credential,
        Func<ModelBinding, CancellationToken, ValueTask<IChatClient>> resolveFallback)
    {
        // The provider returns the RAW client; the entire pipeline is assembled here.
        IChatClient chatClient = provider.CreateChatClient(binding, credential);

        // The concurrency limiter sits closest to the wire: it must see every
        // REAL network call, including every turn of the tool-call loop, not
        // just one call per agent turn — the same "does it need every call?"
        // question K-320 asks about the content guard. When no limit is
        // configured, ProviderConcurrencyLimiter.AcquireAsync returns null and
        // this line does nothing.
        if (_concurrencyLimiter is not null)
        {
            chatClient = new ProviderConcurrencyLimitingChatClient(binding.Provider, chatClient, _concurrencyLimiter);
        }

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

        // 🚨 The fallback chain sits OUTSIDE the circuit breaker (phase 62,
        // F-44): trying a fallback link requires first seeing that the
        // primary's circuit is open, which only the ring outside it can see.
        // It sits INSIDE content-filter detection: a provider-filtered
        // response is not an exception at this layer, only a ChatResponse
        // with ChatFinishReason.ContentFilter, so it passes straight through
        // and the outer detector makes the ONE filtering decision for
        // whichever link actually answered — see FallbackChatClient's remarks.
        // Empty by default (K1): with no configured fallback this line does
        // nothing, and today's "an open circuit throws" behavior is unchanged.
        if (binding.Fallbacks.Count > 0)
        {
            chatClient = new FallbackChatClient(binding, chatClient, resolveFallback);
        }

        // Content-filter detection sits OUTERMOST — outside the circuit breaker.
        // A safety filter means the provider is healthy; if it were inside, the
        // exception it throws would increase the consecutive-failure counter
        // and a handful of content-filtered requests in a row would close the
        // circuit on the provider.
        return new ContentFilterDetectingChatClient(binding.Provider, chatClient);
    }
}
