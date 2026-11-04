using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Holds the telemetry clients of the model-call pipelines, one for each provider and endpoint, so the meter and the
/// activity source of a model call are made once per identity and not once per pipeline.
/// </summary>
/// <remarks>
/// <para>
/// The framework's <see cref="OpenTelemetryChatClient"/> makes a <see cref="System.Diagnostics.Metrics.Meter"/>, an
/// <see cref="ActivitySource"/> and four histograms when it is built, and the runtime keeps a meter in a process-wide list
/// until it is disposed. A pipeline is built for every agent a tenant compiles, for every validation and for every judge
/// client, and nothing disposes it: a compiled agent leaves the cache by being dropped, because a run may still hold it.
/// A client made for every pipeline is a leak that no bound on the cache closes.
/// </para>
/// <para>
/// Disposing the client when the pipeline goes is not the fix either. The OpenTelemetry SDK was seen to fold instruments
/// of one identity into one series only while a live instrument feeds it: a disposed meter retired its series. A lane is
/// therefore never disposed while the pool lives.
/// </para>
/// <para>
/// A pipeline does not borrow a lane. It calls the lane's client with the target of that one call, the part of the
/// pipeline below the telemetry ring, carried on the options of the call (<see cref="RoutedChatOptions"/>). The lane
/// keeps nothing about a call, so any number of calls from any number of tenants share it, and it cannot send a call to
/// the pipeline of another tenant: the only target it ever sees is the one that came with the call.
/// </para>
/// <para>
/// The framework's client reads what the chat client under it says about itself (the provider, the default model and the
/// host and port of the endpoint) once, when it is built, and writes it on every span and metric afterwards. Lanes are
/// therefore pooled by <see cref="ChatTelemetryKey"/>: the provider and the endpoint. The default model is not part of
/// the key. It is free text of the agent definition, so a key that held it would let a tenant add a lane with every model
/// name it writes; the client reads the model of a call from the options of the call before it reads its default, and
/// <see cref="SharedTelemetryChatClient"/> puts the default of its own pipeline there.
/// </para>
/// </remarks>
internal sealed class ChatTelemetryLanes : IDisposable
{
    private readonly string _sourceName;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ILogger? _logger;
    private readonly Func<ChatTelemetryKey, ChatClientMetadata?, ChatTelemetryLane> _createLane;
    private readonly ConcurrentDictionary<ChatTelemetryKey, ChatTelemetryLane> _lanes = [];
    private readonly Lock _gate = new();
    private int _disposed;

    /// <summary>Initializes a pool of lanes that write to one telemetry source.</summary>
    /// <param name="sourceName">The activity source and meter name of every lane.</param>
    /// <param name="loggerFactory">Where the lanes log, or <see langword="null"/> for no logger.</param>
    /// <param name="createLane">Builds a lane; the framework's telemetry client when omitted. A seam for tests only.</param>
    /// <exception cref="ArgumentException"><paramref name="sourceName"/> is <see langword="null"/> or white space.</exception>
    internal ChatTelemetryLanes(
        string sourceName,
        ILoggerFactory? loggerFactory = null,
        Func<ChatTelemetryKey, ChatClientMetadata?, ChatTelemetryLane>? createLane = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        _sourceName = sourceName;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<ChatTelemetryLanes>();
        _createLane = createLane ?? NewLane;
    }

    /// <summary>Gets the number of lanes made and not yet disposed.</summary>
    internal int LaneCount => _lanes.Count;

    /// <summary>
    /// Puts the shared telemetry client above <paramref name="pipeline"/>: the part of a model-call pipeline that writes
    /// to the model providers and everything beneath the telemetry ring.
    /// </summary>
    /// <param name="pipeline">The pipeline below the telemetry ring.</param>
    /// <returns>
    /// The ring that writes the spans and the metrics of every call and then calls <paramref name="pipeline"/>. When the pool
    /// is disposed, or its lane cannot be built, <paramref name="pipeline"/> itself: the run goes on without a model span.
    /// </returns>
    internal IChatClient Wrap(IChatClient pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        if (Volatile.Read(ref _disposed) != 0)
        {
            return pipeline;
        }

        // 🚨 Telemetry is observability, and observability never breaks the run. A lane that cannot be built costs the
        // run its model spans and nothing else; the next pipeline tries again, because nothing about the failure is
        // remembered.
        try
        {
            var metadata = pipeline.GetService<ChatClientMetadata>();

            return LaneFor(ChatTelemetryKey.Of(metadata), metadata) is { } lane
                ? new SharedTelemetryChatClient(pipeline, lane.Client, metadata?.DefaultModelId)
                : pipeline;
        }
        catch (Exception exception)
        {
            if (_logger is not null && _logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(exception, "A model call telemetry lane could not be built; the pipeline goes on without it.");
            }

            return pipeline;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        lock (_gate)
        {
            foreach (var lane in _lanes.Values)
            {
                lane.Dispose();
            }

            _lanes.Clear();
        }
    }

    private ChatTelemetryLane NewLane(ChatTelemetryKey key, ChatClientMetadata? metadata)
        => new(key, metadata, _sourceName, _loggerFactory?.CreateLogger(typeof(OpenTelemetryChatClient)));

    private ChatTelemetryLane? LaneFor(ChatTelemetryKey key, ChatClientMetadata? metadata)
    {
        if (_lanes.TryGetValue(key, out var existing))
        {
            return existing;
        }

        // 🚨 A lane is made under a lock and not by GetOrAdd(factory): two pipelines that miss together would each build
        // a client, the loser of the race would be dropped without a dispose, and that is the leak this pool exists to
        // stop.
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return null;
            }

            if (_lanes.TryGetValue(key, out existing))
            {
                return existing;
            }

            var lane = _createLane(key, metadata);
            _lanes[key] = lane;

            return lane;
        }
    }
}

/// <summary>The framework's telemetry client for one provider and endpoint, and the router under it.</summary>
internal sealed class ChatTelemetryLane : IDisposable
{
    internal ChatTelemetryLane(ChatTelemetryKey key, ChatClientMetadata? metadata, string sourceName, ILogger? logger)
    {
        Key = key;
        Client = new OpenTelemetryChatClient(
            new TelemetryRoutingChatClient(metadata is null ? null : MinimalMetadata(metadata)),
            logger,
            sourceName);
    }

    /// <summary>Gets the identity the lane was built for.</summary>
    internal ChatTelemetryKey Key { get; }

    /// <summary>Gets the client that writes the spans and the metrics. It belongs to the pool: nothing else disposes it.</summary>
    internal OpenTelemetryChatClient Client { get; }

    /// <inheritdoc />
    public void Dispose() => Client.Dispose();

    /// <summary>
    /// What the framework's client reads and nothing else: the provider, and the host and port of the endpoint. The
    /// default model is left out on purpose (see <see cref="ChatTelemetryLanes"/>), and so is everything else in the
    /// address of the endpoint (a user, a path, a query), which the lane has no use for and would keep alive.
    /// </summary>
    private static ChatClientMetadata MinimalMetadata(ChatClientMetadata metadata)
        => new(
            metadata.ProviderName,
            metadata.ProviderUri is { } uri ? new UriBuilder(uri.Scheme, uri.Host, uri.Port).Uri : null,
            defaultModelId: null);
}

/// <summary>What the framework's telemetry client reads from the chat client under it when it is built, apart from the default model.</summary>
/// <param name="Provider">The provider name of the chat client.</param>
/// <param name="ServerAddress">The host of the chat client's endpoint.</param>
/// <param name="ServerPort">The port of the chat client's endpoint, or 0 when it names none.</param>
internal readonly record struct ChatTelemetryKey(string? Provider, string? ServerAddress, int ServerPort)
{
    /// <summary>Reads the key of a chat client the way the framework's telemetry client would.</summary>
    /// <param name="metadata">What the chat client says about itself, if anything.</param>
    /// <returns>Its key.</returns>
    internal static ChatTelemetryKey Of(ChatClientMetadata? metadata)
        => new(metadata?.ProviderName, metadata?.ProviderUri?.Host, metadata?.ProviderUri?.Port ?? 0);
}

/// <summary>
/// The ring that sits where the framework's telemetry client used to sit in a pipeline. It owns the part of the
/// pipeline below it, and every call goes through the pool's shared client with that part as its target.
/// </summary>
/// <remarks>
/// Disposing it disposes the pipeline below and leaves the shared client alone: the client belongs to the pool.
/// </remarks>
internal sealed class SharedTelemetryChatClient : DelegatingChatClient
{
    private readonly OpenTelemetryChatClient _lane;
    private readonly string? _defaultModelId;

    /// <summary>Initializes the ring.</summary>
    /// <param name="target">The pipeline below the ring.</param>
    /// <param name="lane">The pool's client for the identity of <paramref name="target"/>.</param>
    /// <param name="defaultModelId">The default model <paramref name="target"/> reports, or <see langword="null"/>.</param>
    internal SharedTelemetryChatClient(IChatClient target, OpenTelemetryChatClient lane, string? defaultModelId)
        : base(target)
    {
        _lane = lane;
        _defaultModelId = defaultModelId;
    }

    /// <inheritdoc />
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => _lane.GetResponseAsync(messages, new RoutedChatOptions(InnerClient, options, _defaultModelId), cancellationToken);

    /// <inheritdoc />
    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => _lane.GetStreamingResponseAsync(messages, new RoutedChatOptions(InnerClient, options, _defaultModelId), cancellationToken);

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        // The source the spans are written to is the one thing the ring used to answer for itself. The client is not
        // handed out: it is shared, and a flag set on it would reach every tenant's pipeline.
        return serviceType == typeof(ActivitySource)
            ? _lane.GetService(serviceType, serviceKey)
            : base.GetService(serviceType, serviceKey);
    }
}

/// <summary>
/// The client under a lane's telemetry client. It forwards every call to the target that came with the call, and answers
/// the one question the telemetry client asks of it when it is built.
/// </summary>
/// <param name="metadata">What the lane's telemetry client reads when it is built, or <see langword="null"/> for a chat client that says nothing.</param>
internal sealed class TelemetryRoutingChatClient(ChatClientMetadata? metadata) : IChatClient
{
    /// <inheritdoc />
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var route = RoutedChatOptions.Of(options);

        return route.Target.GetResponseAsync(messages, route.Original, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var route = RoutedChatOptions.Of(options);

        return route.Target.GetStreamingResponseAsync(messages, route.Original, cancellationToken);
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (serviceKey is not null)
        {
            return null;
        }

        if (serviceType.IsInstanceOfType(this))
        {
            return this;
        }

        return metadata is not null && serviceType.IsInstanceOfType(metadata) ? metadata : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}

/// <summary>
/// The options of one model call on its way through a lane: the target of the call, the options the caller gave, and a
/// copy of those for the telemetry client to read.
/// </summary>
/// <remarks>
/// <para>
/// This is how a call tells the lane where it goes, and the only way. It is a <see cref="ChatOptions"/> of its own type, not
/// an entry in <see cref="ChatOptions.AdditionalProperties"/>: the telemetry client writes every additional property
/// to the span when it records message content, so an entry would leak the target and change the shape of the span. Only
/// <see cref="SharedTelemetryChatClient"/> makes one, and only for the call it is making; the type is internal, so a caller
/// cannot offer a target of its own.
/// </para>
/// <para>
/// The copy is a plain copy of the caller's options: the telemetry client reads them and writes the same tags it wrote
/// when it read the caller's options. The one difference is the model: a call that names none carries the default of the
/// pipeline it goes to, which is where the client used to take it from. The target receives the caller's options as they
/// were, so nothing under the telemetry ring sees the copy.
/// </para>
/// </remarks>
internal sealed class RoutedChatOptions : ChatOptions
{
    /// <summary>Initializes the options of one call.</summary>
    /// <param name="target">The pipeline the call goes to.</param>
    /// <param name="original">The options the caller gave, or <see langword="null"/> when it gave none.</param>
    /// <param name="defaultModelId">The model a call that names none is made for, or <see langword="null"/>.</param>
    internal RoutedChatOptions(IChatClient target, ChatOptions? original, string? defaultModelId)
        : base(original)
    {
        Target = target;
        Original = original;
        ModelId ??= defaultModelId;
    }

    private RoutedChatOptions(RoutedChatOptions other)
        : base(other)
    {
        Target = other.Target;
        Original = other.Original;
    }

    /// <summary>Gets the pipeline the call goes to.</summary>
    internal IChatClient Target { get; }

    /// <summary>Gets the options the caller gave, which the target receives.</summary>
    internal ChatOptions? Original { get; }

    /// <inheritdoc />
    public override ChatOptions Clone() => new RoutedChatOptions(this);

    /// <summary>Reads the route of a call.</summary>
    /// <param name="options">The options that reached the lane.</param>
    /// <returns>The route.</returns>
    /// <exception cref="InvalidOperationException">
    /// The options are not the ones the ring made. A lane has no target of its own to fall back on, so it refuses the call.
    /// </exception>
    internal static RoutedChatOptions Of(ChatOptions? options)
        => options as RoutedChatOptions
            ?? throw new InvalidOperationException(
                "A model call reached a telemetry lane without the target it was made for; a lane never picks one.");
}
