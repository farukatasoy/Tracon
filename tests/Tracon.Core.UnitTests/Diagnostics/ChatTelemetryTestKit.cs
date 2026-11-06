using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon.Core.UnitTests.Diagnostics;

/// <summary>
/// Listens to the spans of one source name, and makes pools that write to it. Every probe has a source name of its
/// own, so a listener bound to it sees the lanes of this test and no other test's.
/// </summary>
internal sealed class ChatSpanProbe : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly List<Activity> _spans = [];
    private readonly Lock _gate = new();
    private int _sourcesCreated;

    public ChatSpanProbe()
    {
        Source = "chat-lane-test-" + Guid.NewGuid().ToString("N");

        _listener = new ActivityListener
        {
            ShouldListenTo = source =>
            {
                if (!string.Equals(source.Name, Source, StringComparison.Ordinal))
                {
                    return false;
                }

                Interlocked.Increment(ref _sourcesCreated);

                return true;
            },
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (_gate)
                {
                    _spans.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>Gets the source name the pools of this probe write to.</summary>
    public string Source { get; }

    /// <summary>Gets the number of activity sources with this probe's name made so far: one for each lane.</summary>
    public int SourcesCreated => Volatile.Read(ref _sourcesCreated);

    public ChatTelemetryLanes NewLanes(
        ILoggerFactory? loggerFactory = null,
        Func<ChatTelemetryKey, ChatClientMetadata?, ChatTelemetryLane>? createLane = null,
        int maxIdentities = TraconObservabilityOptions.DefaultMaxTelemetryLaneCardinality)
        => new(Source, loggerFactory, createLane, maxIdentities);

    /// <summary>Gets the spans written so far, in the order they ended.</summary>
    public List<Activity> Spans()
    {
        lock (_gate)
        {
            return [.. _spans];
        }
    }

    public void Dispose() => _listener.Dispose();
}

/// <summary>
/// A model-call pipeline that answers with a fixed text, tells the test what reached it, and can be made to wait or to
/// fail. The text is the identity of its tenant in the tests that mix tenants.
/// </summary>
internal sealed class ScriptedPipeline(
    string answer,
    ChatClientMetadata? metadata = null,
    Task? gate = null,
    Exception? failure = null,
    string? responseModel = null) : IChatClient
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public bool Disposed { get; private set; }

    /// <summary>Gets the options the last call brought, as the pipeline received them.</summary>
    public ChatOptions? LastOptions { get; private set; }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _calls);
        LastOptions = options;

        if (gate is not null)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (failure is not null)
        {
            throw failure;
        }

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, answer))
        {
            ModelId = responseModel,
            Usage = new UsageDetails { InputTokenCount = 7, OutputTokenCount = 3 },
        };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _calls);
        LastOptions = options;

        if (gate is not null)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        yield return new ChatResponseUpdate(ChatRole.Assistant, answer + "-1") { ModelId = responseModel };

        if (failure is not null)
        {
            throw failure;
        }

        yield return new ChatResponseUpdate(ChatRole.Assistant, answer + "-2")
        {
            ModelId = responseModel,
            Contents = [new TextContent(answer + "-2"), new UsageContent(new UsageDetails { InputTokenCount = 7, OutputTokenCount = 3 })],
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceKey is not null)
        {
            return null;
        }

        if (metadata is not null && serviceType.IsInstanceOfType(metadata))
        {
            return metadata;
        }

        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose() => Disposed = true;
}

/// <summary>A logger factory whose every logger keeps the warnings and above it was asked to write.</summary>
internal sealed class WarningLogFactory : ILoggerFactory
{
    private readonly WarningLog _log = new();

    public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Entries => _log.Entries;

    public ILogger CreateLogger(string categoryName) => _log;

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class WarningLog : ILogger
    {
        private readonly List<(LogLevel Level, string Message, Exception? Exception)> _entries = [];
        private readonly Lock _gate = new();

        public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Entries
        {
            get
            {
                lock (_gate)
                {
                    return [.. _entries];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            lock (_gate)
            {
                _entries.Add((logLevel, formatter(state, exception), exception));
            }
        }
    }
}
