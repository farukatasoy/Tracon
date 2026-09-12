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
/// The ordering lives here rather than in the HTTP layer so it is stated once. In
/// particular the concurrency limit is checked <strong>before</strong> the provider
/// is called: a session that is created and then refused has already started
/// billing, so a limit enforced afterwards costs the consumer money on every
/// rejected request.
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
        TimeProvider? timeProvider = null)
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

        var host = new LiveVoiceSessionHost(
            _provider,
            request,
            agent.Agent,
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
            var handle = await host.CreateAsync(sdpOffer, voice, cancellationToken).ConfigureAwait(false);

            await host.AttachAsync(cancellationToken).ConfigureAwait(false);

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

    private readonly record struct AIAgentHandle(AIAgent Agent, AgentSession Session);
}
