using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Why a live voice session could not be opened.</summary>
internal enum LiveVoiceLaunchFailure
{
    /// <summary>No <see cref="ILiveVoiceProvider"/> is registered.</summary>
    ProviderMissing = 0,

    /// <summary>The tenant already holds as many sessions as it may.</summary>
    LimitReached = 1,

    /// <summary>There is no agent by that name.</summary>
    AgentNotFound = 2,

    /// <summary>The agent could not be compiled, or the session could not be opened.</summary>
    AgentFailed = 3,

    /// <summary>
    /// The tenant's provider policy refuses the session: its egress policy does not
    /// allow the live provider, its own credential for the provider has no value or
    /// carries an endpoint override, or the provider cannot use that credential.
    /// </summary>
    TenantPolicyRefused = 4,
}

/// <summary>The outcome of trying to open a live voice session.</summary>
/// <param name="Host">The session, when one was opened.</param>
/// <param name="Handle">The provider's handle, when a session was opened.</param>
/// <param name="Failure">Why it failed, when it did.</param>
/// <param name="Detail">A message safe to return to the caller.</param>
internal readonly record struct LiveVoiceLaunchResult(
    LiveVoiceSessionHost? Host,
    LiveVoiceSessionHandle? Handle,
    LiveVoiceLaunchFailure? Failure,
    string? Detail);

/// <summary>Opens live voice sessions: resolve, create, attach, register.</summary>
/// <remarks>
/// <para>
/// The ordering lives here rather than in the HTTP layer so it is stated once. In
/// particular the concurrency limit is checked <strong>before</strong> the provider
/// is called: a session that is created and then refused has already started
/// billing, so a limit enforced afterwards costs the consumer money on every
/// rejected request.
/// </para>
/// <para>
/// The tenant's provider policy comes first of all, exactly as before a model call:
/// the egress policy must allow <see cref="ILiveVoiceProvider.ProviderName"/>, and a
/// tenant with its own binding for that provider gets its own credential on both
/// provider calls. A binding whose key has no value, a binding with an endpoint
/// override, or a provider that cannot use a tenant credential refuses the
/// session; it never opens on the configured key, which would bill the wrong
/// account.
/// </para>
/// </remarks>
internal sealed class LiveVoiceSessionLauncher
{
    private readonly IAgentCatalog _catalog;
    private readonly AgentSessionManager _sessions;
    private readonly ChatHistoryProvider _chatHistory;
    private readonly IVoiceSessionStore _store;
    private readonly IOptionsMonitor<VoiceLiveOptions> _options;
    private readonly VoiceDurationPricing _pricing;
    private readonly ContentGuardPipeline? _guard;
    private readonly ILiveVoiceProvider? _provider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ITenantEgressPolicyStore? _egressPolicies;
    private readonly ITenantProviderBindingStore? _providerBindings;
    private readonly TenantProviderCredentialResolver? _credentialResolver;

    /// <summary>Creates a launcher.</summary>
    public LiveVoiceSessionLauncher(
        IAgentCatalog catalog,
        AgentSessionManager sessions,
        ChatHistoryProvider chatHistory,
        IVoiceSessionStore store,
        IOptionsMonitor<VoiceLiveOptions> options,
        VoiceDurationPricing pricing,
        LiveVoiceSessionRegistry registry,
        ILoggerFactory loggerFactory,
        ILiveVoiceProvider? provider,
        ContentGuardPipeline? guard,
        TimeProvider? timeProvider = null,
        ITenantEgressPolicyStore? egressPolicies = null,
        ITenantProviderBindingStore? providerBindings = null,
        TenantProviderCredentialResolver? credentialResolver = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(chatHistory);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(pricing);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _catalog = catalog;
        _sessions = sessions;
        _chatHistory = chatHistory;
        _store = store;
        _options = options;
        _pricing = pricing;
        Registry = registry;
        _loggerFactory = loggerFactory;
        _provider = provider;
        _guard = guard;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _egressPolicies = egressPolicies;
        _providerBindings = providerBindings;
        _credentialResolver = credentialResolver;
    }

    /// <summary>Gets whether a live voice provider is registered.</summary>
    public bool IsReady => _provider is not null;

    /// <summary>Gets the session registry.</summary>
    public LiveVoiceSessionRegistry Registry { get; }

    /// <summary>Gets the live layer's current options.</summary>
    public VoiceLiveOptions Options => _options.CurrentValue;

    /// <summary>Opens a session.</summary>
    /// <param name="request">Who is opening it, for what agent, in which session.</param>
    /// <param name="sdpOffer">The media peer's SDP offer.</param>
    /// <param name="voice">The requested output voice, when the caller named one.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The outcome.</returns>
    public async Task<LiveVoiceLaunchResult> LaunchAsync(
        LiveVoiceSessionRequest request,
        string sdpOffer,
        string? voice,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_provider is null)
        {
            return new LiveVoiceLaunchResult(null, null, LiveVoiceLaunchFailure.ProviderMissing, null);
        }

        var options = _options.CurrentValue;

        // Before anything else - no session row, no slot, no provider call - for a
        // tenant whose policy refuses the provider (F-306).
        ModelProviderCredential? credential;

        try
        {
            credential = await ResolveTenantAccessAsync(_provider, request.TenantId, cancellationToken).ConfigureAwait(false);
        }
        catch (TraconException exception)
        {
            return new LiveVoiceLaunchResult(null, null, LiveVoiceLaunchFailure.TenantPolicyRefused, exception.Message);
        }

        AIAgentHandle agent;

        try
        {
            var resolved = await _catalog
                .ResolveAsync(request.AgentName, culture: null, cancellationToken)
                .ConfigureAwait(false);

            if (resolved is null)
            {
                return new LiveVoiceLaunchResult(
                    null,
                    null,
                    LiveVoiceLaunchFailure.AgentNotFound,
                    $"There is no agent named '{request.AgentName}'.");
            }

            var session = await _sessions
                .GetOrCreateSessionAsync(resolved, request.SessionId, cancellationToken)
                .ConfigureAwait(false);

            agent = new AIAgentHandle(resolved, session);
        }
        catch (TraconException exception)
        {
            return new LiveVoiceLaunchResult(null, null, LiveVoiceLaunchFailure.AgentFailed, exception.Message);
        }

        // F-310: the agent resolved above opens the session and writes its history;
        // every delegation resolves the same name again (LiveVoiceDelegationRunner).
        var catalog = _catalog;
        var agentName = request.AgentName;

        var host = new LiveVoiceSessionHost(
            _provider,
            request,
            agent.Agent,
            cancellationToken => catalog.ResolveAsync(agentName, culture: null, cancellationToken),
            agent.Session,
            options,
            _store,
            new VoiceHistoryWriter(_chatHistory, _sessions, _loggerFactory.CreateLogger<VoiceHistoryWriter>()),
            _pricing,
            _guard,
            _loggerFactory.CreateLogger<LiveVoiceSessionHost>(),
            _timeProvider);

        // 🚨 The slot is reserved BEFORE the provider call, and reserving it IS the
        // limit check — one compare-and-swap, not a read followed by an add. See the
        // class remarks: a session that is created and then refused is still billed,
        // and a limit that two simultaneous requests can both pass is not a limit.
        if (!Registry.TryAdd(host, options.MaxConcurrentSessionsPerTenant))
        {
            return new LiveVoiceLaunchResult(
                null,
                null,
                LiveVoiceLaunchFailure.LimitReached,
                $"A tenant may hold at most {options.MaxConcurrentSessionsPerTenant} live voice sessions open.");
        }

        try
        {
            var handle = await host.CreateAsync(sdpOffer, voice, credential, cancellationToken).ConfigureAwait(false);

            await host.AttachAsync(credential, cancellationToken).ConfigureAwait(false);

            return new LiveVoiceLaunchResult(host, handle, null, null);
        }
        catch
        {
            // 🚨 The record is written even here: a provider that created a session
            // and then failed to attach has already started billing.
            await Registry.CloseAsync(host, VoiceSessionEndReason.Error).ConfigureAwait(false);

            throw;
        }
    }

    /// <summary>Applies the tenant's egress policy and resolves its own credential for the live provider.</summary>
    /// <returns>The tenant's credential, or <see langword="null"/> for the provider's configured one.</returns>
    /// <exception cref="TraconException">The tenant's policy refuses the session.</exception>
    private async ValueTask<ModelProviderCredential?> ResolveTenantAccessAsync(
        ILiveVoiceProvider provider,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var providerName = provider.ProviderName;

        if (_egressPolicies is not null)
        {
            var policy = await _egressPolicies.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);

            TenantProviderAccess.EnsureAllowed(policy, tenantId, providerName, TenantProviderCallKind.LiveVoice);
        }

        var credential = await TenantProviderAccess
            .ResolveCredentialAsync(_providerBindings, _credentialResolver, tenantId, providerName, cancellationToken)
            .ConfigureAwait(false);

        if (credential is null)
        {
            return null;
        }

        if (!provider.SupportsTenantCredentials)
        {
            throw new TraconException(
                $"Tenant '{tenantId}' has its own credential for '{providerName}', and the live voice provider " +
                "cannot use it. The session is not opened on the provider's configured credential.");
        }

        // A binding's endpoint override names a chat API base, not a live session
        // surface; no live provider is given one. Refused here, before the slot and
        // the session record, rather than sent somewhere the tenant did not choose.
        if (credential.Endpoint is { Length: > 0 })
        {
            throw new TraconException(
                $"Tenant '{tenantId}' has a '{providerName}' binding with an endpoint override. A live voice " +
                "session uses the provider's configured live endpoint and cannot honour that override, so it is " +
                "not opened.");
        }

        return credential;
    }

    private readonly record struct AIAgentHandle(AIAgent Agent, AgentSession Session);
}
