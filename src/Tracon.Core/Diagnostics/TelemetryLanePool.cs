using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Lends out agent-framework telemetry lanes, so the telemetry meter and activity source of a run are made once per
/// lane and not once per run.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="OpenTelemetryAgent"/> makes a <see cref="System.Diagnostics.Metrics.Meter"/>, an
/// <see cref="System.Diagnostics.ActivitySource"/> and four histograms when it is built, and the runtime keeps a meter
/// in a process-wide list until it is disposed. An agent built for every run leaves one of each behind per run.
/// </para>
/// <para>
/// Disposing the agent after the run is not the fix. The OpenTelemetry SDK was seen to fold instruments of one identity
/// into one series only while a live instrument feeds it: a disposed meter retired its series, so every run became a
/// series of its own that held one measurement, and an idle collection returned nothing. A lane is therefore never
/// disposed while the pool lives.
/// </para>
/// <para>
/// A lane belongs to one run at a time, from <see cref="TryRent"/> to <see cref="Return"/>. That is what lets the lane
/// aim at the agent of the run through a plain field: no second run can read it, and the options and the session of
/// the run reach the agent as they came.
/// </para>
/// <para>
/// The framework's telemetry agent reads what its chat client says about itself (the provider, the default model and the
/// host and port of the endpoint) once, when it is built, and writes it on every span and metric afterwards. Lanes are
/// therefore pooled by <see cref="TelemetryKey"/>: a lane is built for one such identity and only runs of agents with
/// that identity borrow it. The number of lanes is the sum, over the identities in use, of the runs of that identity in
/// flight at its busiest moment.
/// </para>
/// <para>
/// The default model and the endpoint of that identity are free text that a tenant chooses, and a lane is never disposed.
/// The pool therefore accepts at most a fixed number of identities that carry a model or an endpoint
/// (<see cref="TraconObservabilityOptions.MaxTelemetryLaneCardinality"/>). A run of any further identity borrows a lane of
/// its providers that names no model and no endpoint: it still writes its span and its metrics, and loses those labels.
/// An identity that was accepted keeps its lanes for the life of the pool, because taking a lane back would turn one
/// series into two (see above).
/// </para>
/// </remarks>
internal sealed class TelemetryLanePool : IDisposable
{
    private readonly string _sourceName;
    private readonly bool _enableSensitiveData;
    private readonly ILogger? _logger;
    private readonly Func<TelemetryIdentity, string, bool, TelemetryLane> _createLane;
    private readonly ConcurrentDictionary<TelemetryKey, ConcurrentStack<TelemetryLane>> _idle = [];
    private readonly int _maxIdentities;
    private int _lanes;
    private int _admitted;
    private int _overflowed;
    private int _overflowWarned;
    private int _disposed;

    /// <summary>Initializes a pool of lanes that write to one telemetry source.</summary>
    /// <param name="sourceName">The activity source and meter name of every lane.</param>
    /// <param name="enableSensitiveData">Whether the lanes record message content.</param>
    /// <param name="logger">Where a lane that could not be built is reported, or <see langword="null"/> to report nowhere.</param>
    /// <param name="createLane">Builds a lane; the framework's telemetry agent when omitted. A seam for tests only.</param>
    /// <param name="maxIdentities">How many identities that carry a model or an endpoint are accepted; the rest share the lanes of their providers.</param>
    /// <exception cref="ArgumentException"><paramref name="sourceName"/> is <see langword="null"/> or white space.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxIdentities"/> is negative.</exception>
    internal TelemetryLanePool(
        string sourceName,
        bool enableSensitiveData,
        ILogger? logger = null,
        Func<TelemetryIdentity, string, bool, TelemetryLane>? createLane = null,
        int maxIdentities = TraconObservabilityOptions.DefaultMaxTelemetryLaneCardinality)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentOutOfRangeException.ThrowIfNegative(maxIdentities);
        _sourceName = sourceName;
        _enableSensitiveData = enableSensitiveData;
        _logger = logger;
        _createLane = createLane ?? NewLane;
        _maxIdentities = maxIdentities;
    }

    private static TelemetryLane NewLane(TelemetryIdentity identity, string sourceName, bool enableSensitiveData)
        => new(identity, sourceName, enableSensitiveData);

    /// <summary>Gets the number of lanes made and not yet disposed.</summary>
    internal int LaneCount => Volatile.Read(ref _lanes);

    /// <summary>Gets the number of identities that carry a model or an endpoint and were accepted, which never goes above the limit.</summary>
    internal int AdmittedIdentities => Volatile.Read(ref _admitted);

    /// <summary>Gets the number of runs that borrowed a lane of their providers because the limit was reached.</summary>
    internal int OverflowedCalls => Volatile.Read(ref _overflowed);

    /// <summary>Gets the number of lanes waiting for a run.</summary>
    internal int IdleCount => _idle.Values.Sum(static stack => stack.Count);

    /// <summary>Gets the number of waiting lanes that still point at an agent, which keeps that agent alive. It is zero unless a lane came back uncleaned.</summary>
    internal int IdleAimedCount => _idle.Values.Sum(static stack => stack.Count(static lane => lane.Target is not null));

    /// <summary>Lends a lane aimed at <paramref name="target"/>.</summary>
    /// <param name="identity">What the chat client of the target says about itself.</param>
    /// <param name="target">The agent the run calls.</param>
    /// <returns>
    /// A lane that belongs to the caller until <see cref="Return"/>, or <see langword="null"/> when the pool is disposed or a
    /// lane could not be built; the caller then runs the agent without telemetry.
    /// </returns>
    internal TelemetryLane? TryRent(TelemetryIdentity identity, AIAgent target)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return null;
        }

        // An identity the pool already holds takes one lookup, as it did before there was a limit; only a key the pool has
        // not seen goes through the admission.
        var key = identity.Key;

        if (!_idle.TryGetValue(key, out var stack))
        {
            key = Admit(key);
            stack = _idle.GetOrAdd(key, static _ => new ConcurrentStack<TelemetryLane>());
        }

        if (!stack.TryPop(out var lane))
        {
            // 🚨 Telemetry is observability, and observability never breaks the run. The caller runs the agent
            // without a lane when this returns null, so a lane that cannot be built costs the run its spans and
            // nothing else; the next run tries again, because nothing about the failure is remembered. An identity
            // that was accepted keeps its place even then: the place belongs to the identity and not to the lane.
            try
            {
                lane = _createLane(key == identity.Key ? identity : identity.ForOverflow(), _sourceName, _enableSensitiveData);
            }
            catch (Exception exception)
            {
                if (_logger is not null && _logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning(exception, "A telemetry lane could not be built; the run goes on without telemetry.");
                }

                return null;
            }

            Interlocked.Increment(ref _lanes);
        }

        lane.Target = target;

        return lane;
    }

    /// <summary>
    /// Says which key a run of <paramref name="key"/> borrows its lane under: its own while the pool has a place for it, or
    /// the key of its providers without a model and an endpoint when it has none.
    /// </summary>
    /// <param name="key">The identity of the agent the run calls.</param>
    /// <returns>The key to borrow under.</returns>
    private TelemetryKey Admit(TelemetryKey key)
    {
        // A key without a model and an endpoint is the key an overflowing identity falls back to. It takes no place, so
        // an agent whose chat client says nothing about itself is never turned away.
        if (!key.NamesModelOrEndpoint || _idle.ContainsKey(key))
        {
            return key;
        }

        while (true)
        {
            var admitted = Volatile.Read(ref _admitted);

            if (admitted >= _maxIdentities)
            {
                // The thread that took the last place for this very key may not have stored it yet; the next run of it
                // finds it. Nothing about the turned-away run is remembered, so no set of overflowing identities grows.
                return _idle.ContainsKey(key) ? key : Overflow(key);
            }

            // The place is taken before the key is stored, as the job-lane tags do: two identities that arrive together
            // cannot both take the last one. A run that loses the race for its own key hands the place back.
            if (Interlocked.CompareExchange(ref _admitted, admitted + 1, admitted) != admitted)
            {
                continue;
            }

            if (!_idle.TryAdd(key, new ConcurrentStack<TelemetryLane>()))
            {
                Interlocked.Decrement(ref _admitted);
            }

            return key;
        }
    }

    private TelemetryKey Overflow(TelemetryKey key)
    {
        Interlocked.Increment(ref _overflowed);

        if (Interlocked.Exchange(ref _overflowWarned, 1) == 0 && _logger is not null && _logger.IsEnabled(LogLevel.Warning))
        {
            // 🚨 The text names neither a host, a port nor a model: they are the tenant's own settings, and a log is not
            // where a tenant's resource name belongs.
            _logger.LogWarning(
                "Agent telemetry keeps lanes for {Limit} models and endpoints, and every further one is written without its model, address and port. " +
                "Raise Tracon:Observability:MaxTelemetryLaneCardinality if the host expects more models or endpoints.",
                _maxIdentities);
        }

        return key.WithoutModelAndEndpoint();
    }

    /// <summary>Takes a lane back and forgets its target.</summary>
    /// <param name="lane">The lane <see cref="TryRent"/> lent.</param>
    internal void Return(TelemetryLane lane)
    {
        ArgumentNullException.ThrowIfNull(lane);

        // A lane that stays aimed at its last agent keeps that agent, its session store and its tools alive.
        lane.Target = null;

        if (Volatile.Read(ref _disposed) != 0)
        {
            Discard(lane);
            return;
        }

        _idle.GetOrAdd(lane.Key, static _ => new ConcurrentStack<TelemetryLane>()).Push(lane);

        // Dispose may have drained the stacks between the check and the push.
        if (Volatile.Read(ref _disposed) != 0)
        {
            DisposeIdle();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        DisposeIdle();
    }

    private void DisposeIdle()
    {
        foreach (var stack in _idle.Values)
        {
            while (stack.TryPop(out var lane))
            {
                Discard(lane);
            }
        }
    }

    private void Discard(TelemetryLane lane)
    {
        lane.Dispose();
        Interlocked.Decrement(ref _lanes);
    }
}

/// <summary>
/// An <see cref="OpenTelemetryAgent"/> and the slot that points it at the agent of one run.
/// </summary>
internal sealed class TelemetryLane : IDisposable
{
    private readonly TelemetrySlot _slot;
    private readonly OpenTelemetryAgent _telemetry;

    internal TelemetryLane(TelemetryIdentity identity, string sourceName, bool enableSensitiveData)
    {
        Key = identity.Key;
        _slot = new TelemetrySlot(identity);

        // MAAI001: OpenTelemetryAgent is marked "evaluation purposes only". The suppression is deliberate. This is the
        // only place that builds one, so a MAF API change only requires an update here.
#pragma warning disable MAAI001
        // The guard keeps a summed response (a loop, an auto-approved tool round) from being rebuilt out of its last
        // call's raw representation. autoWireChatClient is off: the chat client pipeline already writes the model
        // spans, and wiring it again would write each one twice.
        _telemetry = new OpenTelemetryAgent(new AggregatedResponseGuard(_slot), sourceName, autoWireChatClient: false)
        {
            EnableSensitiveData = enableSensitiveData,
        };
#pragma warning restore MAAI001
    }

    /// <summary>Gets the identity the lane was built for, which is the key of the pool stack it waits in.</summary>
    internal TelemetryKey Key { get; }

    /// <summary>Gets the agent a run calls to write its telemetry.</summary>
    internal AIAgent Agent => _telemetry;

    /// <summary>Gets or sets the agent the run behind the lane calls.</summary>
    internal AIAgent? Target
    {
        get => _slot.Target;
        set => _slot.Target = value;
    }

    /// <inheritdoc />
    public void Dispose() => _telemetry.Dispose();
}

/// <summary>
/// The agent a lane's telemetry agent wraps. Every call goes to the agent of the run the lane is lent to; the name, the
/// description and the identity are read from that agent when a span names itself. While no run is aimed at it, the
/// slot answers the two questions the telemetry agent asks when it is built, with the values of the identity the lane
/// was built for.
/// </summary>
internal sealed class TelemetrySlot(TelemetryIdentity identity) : AIAgent
{
    private readonly AIAgentMetadata _agentMetadata = new(identity.Key.AgentProvider);

    /// <summary>
    /// Gets or sets the agent the lane's run calls. The run that holds the lane is the only reader, and the pool's stack
    /// and the awaits of the run order every handoff, so the field needs no fence of its own.
    /// </summary>
    internal AIAgent? Target { get; set; }

    /// <inheritdoc />
    public override string? Name => Target?.Name;

    /// <inheritdoc />
    public override string? Description => Target?.Description;

    /// <inheritdoc />
    protected override string? IdCore => Target?.Id;

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (Target is { } target)
        {
            return target.GetService(serviceType, serviceKey);
        }

        if (serviceKey is not null)
        {
            return base.GetService(serviceType, serviceKey);
        }

        if (serviceType.IsInstanceOfType(_agentMetadata))
        {
            return _agentMetadata;
        }

        return identity.ChatMetadata is { } chatMetadata && serviceType.IsInstanceOfType(chatMetadata)
            ? chatMetadata
            : base.GetService(serviceType, serviceKey);
    }

    /// <inheritdoc />
    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
        => Aimed().CreateSessionAsync(cancellationToken);

    /// <inheritdoc />
    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
        AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
        => Aimed().SerializeSessionAsync(session, jsonSerializerOptions, cancellationToken);

    /// <inheritdoc />
    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
        => Aimed().DeserializeSessionAsync(serializedState, jsonSerializerOptions, cancellationToken);

    /// <inheritdoc />
    protected override Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
        => Aimed().RunAsync(messages, session, options, cancellationToken);

    /// <inheritdoc />
    protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
        => Aimed().RunStreamingAsync(messages, session, options, cancellationToken);

    private AIAgent Aimed()
        => Target ?? throw new InvalidOperationException("The telemetry lane was called while no run was aimed at it.");
}

/// <summary>What the framework's telemetry agent reads from the agent it wraps when it is built, and nothing else.</summary>
/// <param name="AgentProvider">The provider name of the agent's own metadata.</param>
/// <param name="ChatProvider">The provider name of the chat client.</param>
/// <param name="DefaultModelId">The default model of the chat client.</param>
/// <param name="ServerAddress">The host of the chat client's endpoint.</param>
/// <param name="ServerPort">The port of the chat client's endpoint, or 0 when it names none.</param>
internal readonly record struct TelemetryKey(
    string? AgentProvider,
    string? ChatProvider,
    string? DefaultModelId,
    string? ServerAddress,
    int ServerPort)
{
    /// <summary>Gets a value that says whether the key carries a default model or an endpoint, which is what the pool counts.</summary>
    internal bool NamesModelOrEndpoint => DefaultModelId is not null || ServerAddress is not null || ServerPort != 0;

    /// <summary>Gets the key of the lanes that serve every identity of these providers the pool has no place for.</summary>
    /// <returns>The key without its default model, host and port.</returns>
    internal TelemetryKey WithoutModelAndEndpoint() => new(AgentProvider, ChatProvider, DefaultModelId: null, ServerAddress: null, ServerPort: 0);
}

/// <summary>An agent's <see cref="TelemetryKey"/> together with the chat client metadata it was read from.</summary>
internal sealed class TelemetryIdentity
{
    private TelemetryIdentity(TelemetryKey key, ChatClientMetadata? chatMetadata)
    {
        Key = key;
        ChatMetadata = chatMetadata;
    }

    /// <summary>Gets the key lanes are pooled by.</summary>
    internal TelemetryKey Key { get; }

    /// <summary>Gets the metadata a new lane answers with. Another instance with the same key reads the same.</summary>
    internal ChatClientMetadata? ChatMetadata { get; }

    /// <summary>
    /// Makes the identity a lane is built for when the pool has no place for this one: the same providers, and a chat
    /// client that names no default model and no endpoint. The lane must not be told more than that, or its spans would
    /// still carry the address of the identity that was turned away.
    /// </summary>
    /// <returns>The identity of the providers.</returns>
    internal TelemetryIdentity ForOverflow()
        => new(
            Key.WithoutModelAndEndpoint(),
            ChatMetadata is null ? null : new ChatClientMetadata(ChatMetadata.ProviderName, providerUri: null, defaultModelId: null));

    /// <summary>Reads the identity of <paramref name="agent"/> the way the framework's telemetry agent would.</summary>
    /// <param name="agent">The agent a run calls.</param>
    /// <returns>Its identity.</returns>
    internal static TelemetryIdentity Of(AIAgent agent)
    {
        var chat = agent.GetService<ChatClientMetadata>();
        var endpoint = chat?.ProviderUri;

        return new TelemetryIdentity(
            new TelemetryKey(
                agent.GetService<AIAgentMetadata>()?.ProviderName,
                chat?.ProviderName,
                chat?.DefaultModelId,
                endpoint?.Host,
                endpoint?.Port ?? 0),
            chat);
    }
}
