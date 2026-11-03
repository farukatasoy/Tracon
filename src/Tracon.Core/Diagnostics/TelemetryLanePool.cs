using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

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
/// </remarks>
internal sealed class TelemetryLanePool : IDisposable
{
    private readonly string _sourceName;
    private readonly bool _enableSensitiveData;
    private readonly ConcurrentDictionary<TelemetryKey, ConcurrentStack<TelemetryLane>> _idle = [];
    private int _lanes;
    private int _disposed;

    /// <summary>Initializes a pool of lanes that write to one telemetry source.</summary>
    /// <param name="sourceName">The activity source and meter name of every lane.</param>
    /// <param name="enableSensitiveData">Whether the lanes record message content.</param>
    /// <exception cref="ArgumentException"><paramref name="sourceName"/> is <see langword="null"/> or white space.</exception>
    internal TelemetryLanePool(string sourceName, bool enableSensitiveData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        _sourceName = sourceName;
        _enableSensitiveData = enableSensitiveData;
    }

    /// <summary>Gets the number of lanes made and not yet disposed.</summary>
    internal int LaneCount => Volatile.Read(ref _lanes);

    /// <summary>Gets the number of lanes waiting for a run.</summary>
    internal int IdleCount => _idle.Values.Sum(static stack => stack.Count);

    /// <summary>Gets the number of waiting lanes that still point at an agent, which keeps that agent alive. It is zero unless a lane came back uncleaned.</summary>
    internal int IdleAimedCount => _idle.Values.Sum(static stack => stack.Count(static lane => lane.Target is not null));

    /// <summary>Lends a lane aimed at <paramref name="target"/>.</summary>
    /// <param name="identity">What the chat client of the target says about itself.</param>
    /// <param name="target">The agent the run calls.</param>
    /// <returns>A lane that belongs to the caller until <see cref="Return"/>, or <see langword="null"/> when the pool is disposed.</returns>
    internal TelemetryLane? TryRent(TelemetryIdentity identity, AIAgent target)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return null;
        }

        var stack = _idle.GetOrAdd(identity.Key, static _ => new ConcurrentStack<TelemetryLane>());

        if (!stack.TryPop(out var lane))
        {
            lane = new TelemetryLane(identity, _sourceName, _enableSensitiveData);
            Interlocked.Increment(ref _lanes);
        }

        lane.Target = target;

        return lane;
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
    int ServerPort);

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
