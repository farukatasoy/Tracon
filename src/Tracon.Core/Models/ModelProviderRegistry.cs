using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Registry that holds the registered <see cref="IModelProvider"/>
/// implementations by name, and the single place that assembles the model-call pipeline.
/// </summary>
/// <remarks>
/// <para>
/// By default no provider is registered; the <c>Tracon.OpenAI</c> package
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
    private readonly IDistributedCache? _distributedCache;
    private readonly TraconMetrics? _metrics;
    private readonly IProviderRetryClassifier? _retryClassifier;
    private readonly IServiceProvider? _services;

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
    /// <param name="distributedCache">
    /// The backing store for <see cref="ModelBinding.ResponseCache"/>. If
    /// <see langword="null"/>, a binding that enables response caching fails
    /// to compile instead of silently running uncached.
    /// </param>
    /// <param name="metrics">
    /// Records a response-cache hit/miss counter. If <see langword="null"/>,
    /// no cache metric is emitted.
    /// </param>
    /// <param name="retryClassifier">
    /// A consumer-supplied provider retry decision, consulted by
    /// <c>FallbackChatClient</c> before Tracon's built-in rules. If
    /// <see langword="null"/>, the built-in rules alone decide.
    /// </param>
    /// <param name="services">
    /// Resolves <see cref="IRunPricingResolver"/> for <see cref="RunBudgetChatClient"/>
    /// LAZILY, at pipeline-build time rather than here in the constructor.
    /// </param>
    /// <remarks>
    /// <para>
    /// <paramref name="services"/> is deliberately not a direct
    /// <c>IRunPricingResolver?</c> parameter. The default cost resolver itself
    /// depends on <see cref="IModelProviderRegistry"/> (it scans the catalog
    /// for a model's price); resolving it here, while this registry is still
    /// being constructed, is a circular dependency the container cannot
    /// satisfy. The pipeline builder resolves it lazily instead, long after
    /// this constructor returns and the registry singleton is cached, which
    /// breaks the cycle.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="providers"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">The same provider name has been registered more than once.</exception>
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
        TenantProviderCredentialResolver? credentialResolver = null,
        IDistributedCache? distributedCache = null,
        TraconMetrics? metrics = null,
        IProviderRetryClassifier? retryClassifier = null,
        IServiceProvider? services = null)
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
        _distributedCache = distributedCache;
        _metrics = metrics;
        _retryClassifier = retryClassifier;
        _services = services;

        foreach (var provider in providers)
        {
            if (!_providers.TryAdd(provider.Name, provider))
            {
                throw new TraconException(
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
        return BuildPipeline(binding, provider, credential, resolveFallback: CreateChatClientAsync);
    }

    /// <inheritdoc />
    public async ValueTask<IChatClient> CreateSetupChatClientAsync(
        ModelBinding binding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var provider = ResolveProvider(binding);
        var credential = await ResolveSetupCredentialAsync(binding, cancellationToken).ConfigureAwait(false);

        // 🚨 The resolver a fallback link uses to build ITS OWN client matters
        // (independent audit, phase 65): a fallback binding carries its own
        // Provider and must go through the SAME tenant credential/egress
        // resolution as the primary, not the sync/global-only path — otherwise
        // a fallback would silently use the global credential and bypass the
        // tenant's egress policy. CreateChatClientAsync is passed here, so a
        // triggered fallback recurses back into this exact method.
        return BuildPipeline(
            binding,
            provider,
            credential,
            resolveFallback: CreateSetupChatClientAsync);
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

            throw new TraconException(
                $"No model provider named '{binding.Provider}' is registered. Registered providers: {known}. " +
                "For OpenAI, call `builder.AddTracon().UseOpenAI(apiKey)`.");
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
                    throw new TraconException(
                        $"Tenant '{tenantId}' is not allowed to call model provider '{binding.Provider}'. " +
                        $"Allowed providers: {string.Join(", ", policy.AllowedProviders)}.");
                }

                foreach (var fallback in binding.Fallbacks)
                {
                    if (!policy.AllowedProviders.Contains(fallback.Provider, StringComparer.OrdinalIgnoreCase))
                    {
                        throw new TraconException(
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
            ?? throw new TraconException(
                $"Tenant '{tenantId}' has a provider binding for '{binding.Provider}' pointing at configuration key " +
                $"'{tenantBinding.ApiKeyConfigurationName}', but that key has no value. Set it with " +
                $"`dotnet user-secrets set \"{tenantBinding.ApiKeyConfigurationName}\" \"<key>\"` " +
                "or through your configuration provider — the call does NOT fall back to the global key.");
    }

    private async ValueTask<ModelProviderCredential?> ResolveSetupCredentialAsync(
        ModelBinding binding,
        CancellationToken cancellationToken)
    {
        if (_tenantContext is null)
        {
            return null;
        }

        var tenantId = _tenantContext.TenantId;

        if (_tenantEgressPolicies is null)
        {
            return null;
        }

        var policy = await _tenantEgressPolicies.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);

        if (policy is null)
        {
            return null;
        }

        if (!policy.AllowedProviders.Contains(binding.Provider, StringComparer.OrdinalIgnoreCase))
        {
            throw new TraconException(
                $"Tenant '{tenantId}' is not allowed to call model provider '{binding.Provider}'. " +
                $"Allowed providers: {string.Join(", ", policy.AllowedProviders)}.");
        }

        foreach (var fallback in binding.Fallbacks)
        {
            if (!policy.AllowedProviders.Contains(fallback.Provider, StringComparer.OrdinalIgnoreCase))
            {
                throw new TraconException(
                    $"Tenant '{tenantId}' is not allowed to call model provider '{fallback.Provider}' " +
                    $"(used as a fallback for '{binding.Provider}'). " +
                    $"Allowed providers: {string.Join(", ", policy.AllowedProviders)}.");
            }
        }

        return null;
    }

    private ProviderFailureNormalizingChatClient BuildPipeline(
        ModelBinding binding,
        IModelProvider provider,
        ModelProviderCredential? credential,
        Func<ModelBinding, CancellationToken, ValueTask<IChatClient>> resolveFallback)
    {
        // The provider returns the RAW client; the entire pipeline is assembled here.
        IChatClient chatClient;

        try
        {
            chatClient = credential switch
            {
                null => provider.CreateChatClient(binding),
                _ when provider is ITenantCredentialModelProvider tenantCredentialProvider
                    => tenantCredentialProvider.CreateChatClient(binding, credential),
                _ => throw ProviderInvocationException.CredentialUnsupported(binding.Provider),
            };
        }
        catch (Exception exception) when (ProviderFailureNormalizer.ShouldNormalize(exception))
        {
            ProviderFailureNormalizer.Log(_loggerFactory, binding, exception, "construction");
            throw ProviderInvocationException.UpstreamFailure(exception, binding.Provider);
        }

        // Mark only exceptions that originate in the raw SDK client. The
        // outer normalizer can then mask provider failures without hiding a
        // bug thrown by a Tracon guard, cache, or attachment wrapper.
        chatClient = new ProviderFailureTaggingChatClient(chatClient);

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
        //
        // binding.AllowConcurrentToolCalls configures THIS SAME
        // FunctionInvokingChatClient instance - the one that actually drives
        // the tool-call loop. false by default (K1): the loop runs one call
        // at a time exactly as it does today (Phase 81, F-134).
        var pipelineBuilder = chatClient
            .AsBuilder()
            .UseFunctionInvocation(
                _loggerFactory,
                fic => fic.AllowConcurrentInvocation = binding.AllowConcurrentToolCalls);

        // 🚨 The budget ring sits in the SAME slot as the response cache ring
        // below: INSIDE the tool-call loop, OUTSIDE the cache and telemetry
        // (phase 114). A turn the budget blocks never reaches the cache
        // lookup or the real provider, so it produces no 'chat' span - the
        // same as a cache hit today. Unconditional (unlike the cache ring):
        // every pipeline this method builds, including the one context
        // compaction's summarization call uses (ResolveSummarizationChatClient
        // calls this same method), must see it - that is what lets this ring
        // own 100% of the tree's usage accounting instead of splitting it with
        // RunRecordingAgent.CompleteAsync.
        pipelineBuilder = pipelineBuilder.Use(ringInner => new RunBudgetChatClient(
            ringInner,
            binding.Provider,
            binding.Model,
            _services?.GetService<IRunPricingResolver>()));

        // 🚨 The cache ring sits INSIDE the tool-call loop but OUTSIDE
        // telemetry and the content guard (Phase 81, F-45): a cache hit still
        // lets the loop run any FunctionCallContent the cached response
        // carries, but it never reaches UseOpenTelemetry (no 'chat' span, no
        // token/cost record - a hit spends nothing) or the content guard (a
        // hit is not re-inspected; ResponseCacheSettings.Lifetime bounds that
        // window). Enabling caching with no IDistributedCache registered is
        // not silently ignored - the binding fails to compile, naming the
        // missing registration, the same way an unknown ReasoningEffort does.
        if (binding.ResponseCache is { Enabled: true } cacheSettings)
        {
            if (_distributedCache is null)
            {
                throw new TraconException(
                    $"Model '{binding.Provider}/{binding.Model}' enables response caching " +
                    "(ResponseCache.Enabled = true), but no IDistributedCache is registered. Register one, " +
                    "for example `builder.Services.AddDistributedMemoryCache()`, before compiling an agent " +
                    "with response caching turned on.");
            }

            pipelineBuilder = pipelineBuilder.Use(inner => new TraconResponseCachingChatClient(
                inner,
                _distributedCache,
                binding.Provider,
                _tenantContext?.TenantId,
                cacheSettings,
                _metrics,
                _loggerFactory?.CreateLogger<TraconResponseCachingChatClient>()));
        }

        chatClient = pipelineBuilder
            .UseOpenTelemetry(_loggerFactory, TraconDiagnostics.ActivitySourceName)
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
            // 🚨 The circuit's blast radius follows the CREDENTIAL, not the
            // provider. A non-null credential is tenant-supplied (BYOK), so its
            // failures may only ever trip that tenant; the setup-time global
            // credential keeps a shared circuit, because every tenant really
            // does share its fate. Keyed by provider alone, one tenant's invalid
            // key stopped the whole installation.
            var credentialScope = credential is null ? null : _tenantContext?.TenantId;

            chatClient = _circuitBreaker.Wrap(binding.Provider, chatClient, credentialScope);
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
            chatClient = new FallbackChatClient(binding, chatClient, resolveFallback, _retryClassifier, _loggerFactory);
        }

        // Content-filter detection sits OUTERMOST — outside the circuit breaker.
        // A safety filter means the provider is healthy; if it were inside, the
        // exception it throws would increase the consecutive-failure counter
        // and a handful of content-filtered requests in a row would close the
        // circuit on the provider.
        chatClient = new ContentFilterDetectingChatClient(binding.Provider, chatClient);

        // The normalization boundary is the outermost model-call ring. Retry,
        // fallback, circuit, guard, and content-filter decisions must see the
        // original exception graph before a public-safe error replaces it.
        return new ProviderFailureNormalizingChatClient(binding, chatClient, _loggerFactory);
    }
}
