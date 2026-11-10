using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon.Tests.Common;

/// <summary>
/// Streams text chunks through a run's event writer into a real store that sits behind a connection with a fixed round
/// trip, once with every chunk written before the stream continues and once with the chunks written behind it.
/// </summary>
/// <remarks>
/// <para>
/// The round trip is added in front of every write the store is asked for, so the real database still does its own work
/// and the wait is what a network between Tracon and the database adds. Nothing else is simulated: the writer, the
/// store, the statements and the database are the shipped ones.
/// </para>
/// <para>
/// Two things can be asked of it. <see cref="RunAsync"/> adds a round trip of real time and measures how long the stream
/// takes, for the claim that a stream paced by a model in real time is not slowed by the connection: that is a real-time
/// measurement and the one place here that keeps a stopwatch. <see cref="HoldAsync"/> does not measure anything: the
/// connection answers nothing until the test lets it, so that "the stream does not wait for the connection" is a stream
/// that ends while no write has been answered, and "each chunk pays its round trip" is one write for each chunk.
/// </para>
/// <para>
/// Two times are kept by <see cref="RunAsync"/>. The <em>stream time</em> is how long the loop that hands the chunks to the
/// writer takes: what a model whose answer is already waiting would be held up for. The <em>total time</em> adds the end of
/// the run, which writes whatever is still queued.
/// </para>
/// </remarks>
internal static class DeltaStreamOverLatency
{
    /// <summary>What one run of <see cref="RunAsync"/> measured.</summary>
    /// <param name="Behind">Whether the chunks were written behind the stream.</param>
    /// <param name="Chunks">How many chunks the stream handed over.</param>
    /// <param name="Stored">How many chunks the store holds afterwards.</param>
    /// <param name="Contiguous">Whether the sequence numbers the store holds are 0 to n-1, each once.</param>
    /// <param name="StreamTime">How long handing the chunks over took.</param>
    /// <param name="TotalTime">How long it took with the end of the run.</param>
    public sealed record Result(bool Behind, int Chunks, int Stored, bool Contiguous, TimeSpan StreamTime, TimeSpan TotalTime)
    {
        /// <summary>Writes the result as one line, for the record of a measurement.</summary>
        /// <param name="provider">The provider name.</param>
        /// <param name="latency">The round trip that was added.</param>
        /// <returns>The line.</returns>
        public string Describe(string provider, TimeSpan latency)
            => string.Create(
                CultureInfo.InvariantCulture,
                $"{provider}\tlatency={latency.TotalMilliseconds:0.#}ms\tbehind={Behind}\tchunks={Chunks}\tstored={Stored}\tstream={StreamTime.TotalMilliseconds:0.0}ms\ttotal={TotalTime.TotalMilliseconds:0.0}ms");
    }

    /// <summary>Appends a line to the file the <c>TRACON_PROBE_OUT</c> variable names, when it names one.</summary>
    /// <param name="line">The line.</param>
    public static void Record(string line)
    {
        if (Environment.GetEnvironmentVariable("TRACON_PROBE_OUT") is { Length: > 0 } path)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }

    /// <summary>Streams the chunks and measures.</summary>
    /// <param name="store">The real store, over the tenant the caller set.</param>
    /// <param name="behind">Whether to write the chunks behind the stream.</param>
    /// <param name="chunks">How many chunks to stream.</param>
    /// <param name="latency">The round trip added in front of each write.</param>
    /// <param name="modelInterval">How long the model takes between two chunks; zero hands them over as fast as the writer takes them.</param>
    /// <returns>What was measured.</returns>
    public static async Task<Result> RunAsync(IRunStore store, bool behind, int chunks, TimeSpan latency, TimeSpan modelInterval = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        var latent = new LatentRunStore(store, () => new ValueTask(PreciseWaitAsync(latency)));
        var writer = new RunEventWriter(
            latent,
            new TraconRunRecordingOptions { WriteDeltasBehind = behind },
            NullLogger.Instance,
            Guid.NewGuid(),
            metrics: null);

        var total = Stopwatch.StartNew(); // real time: a stream paced in real time by a model, over a real connection

        await writer.StartAsync(new RunStartInfo { RunId = writer.RunId, AgentName = "latency-agent", StartedAt = DateTimeOffset.UtcNow }, "hello");

        var stream = Stopwatch.StartNew(); // real time: a stream paced in real time by a model, over a real connection

        for (var i = 0; i < chunks; i++)
        {
            if (modelInterval > TimeSpan.Zero)
            {
                await PreciseWaitAsync(modelInterval);
            }

            await writer.AppendAsync(new RunEventDraft(RunEventType.MessageDelta) { Text = "a streamed chunk " });
        }

        stream.Stop();

        await writer.CompleteAsync(RunStatus.Completed);
        total.Stop();

        var sequences = new List<long>();
        var stored = 0;

        await foreach (var runEvent in store.ReadEventsAsync(writer.RunId))
        {
            sequences.Add(runEvent.Sequence);

            if (runEvent.Type == RunEventType.MessageDelta)
            {
                stored++;
            }
        }

        return new Result(behind, chunks, stored, sequences.SequenceEqual(Enumerable.Range(0, sequences.Count).Select(static n => (long)n)), stream.Elapsed, total.Elapsed);
    }

    /// <summary>
    /// Waits for a span without a timer: a loop that gives the thread back to the pool until the clock says the span is over.
    /// </summary>
    /// <remarks>
    /// <c>Task.Delay</c> is rounded up to the resolution of the operating system's timer, which is several milliseconds on
    /// some machines; a round trip of two milliseconds that took six would be a different measurement from the one the
    /// test names.
    /// </remarks>
    /// <param name="span">How long to wait.</param>
    /// <returns>The completion task.</returns>
    private static async Task PreciseWaitAsync(TimeSpan span)
    {
        var until = Stopwatch.GetTimestamp() + (long)(span.TotalSeconds * Stopwatch.Frequency); // real time: a stream paced in real time by a model, over a real connection

        while (Stopwatch.GetTimestamp() < until) // real time: a stream paced in real time by a model, over a real connection
        {
            await Task.Yield();
        }
    }

    /// <summary>
    /// Streams the chunks into a store whose connection answers nothing until <see cref="Held.ReleaseAsync"/>, and returns
    /// as soon as the stream has been started; the caller looks at what the stream does while the connection is held.
    /// </summary>
    /// <param name="store">The real store, over the tenant the caller set.</param>
    /// <param name="behind">Whether to write the chunks behind the stream.</param>
    /// <param name="chunks">How many chunks to stream.</param>
    /// <returns>The held stream.</returns>
    public static async Task<Held> HoldAsync(IRunStore store, bool behind, int chunks)
    {
        ArgumentNullException.ThrowIfNull(store);

        var connection = new Connection();
        var writer = new RunEventWriter(
            new LatentRunStore(store, connection.AnswerAsync),
            new TraconRunRecordingOptions { WriteDeltasBehind = behind },
            NullLogger.Instance,
            Guid.NewGuid(),
            metrics: null);

        // The run is opened over a connection that answers; only the chunks meet the held one.
        await writer.StartAsync(new RunStartInfo { RunId = writer.RunId, AgentName = "latency-agent", StartedAt = DateTimeOffset.UtcNow }, "hello");

        connection.Hold();

        var stream = Task.Run(async () =>
        {
            for (var i = 0; i < chunks; i++)
            {
                await writer.AppendAsync(new RunEventDraft(RunEventType.MessageDelta) { Text = "a streamed chunk " });
            }
        });

        return new Held(store, writer, connection, stream, chunks);
    }

    /// <summary>A stream into a store behind a connection that the test answers.</summary>
    public sealed class Held : IAsyncDisposable
    {
        private readonly IRunStore _store;
        private readonly RunEventWriter _writer;
        private readonly Connection _connection;
        private readonly int _chunks;

        internal Held(IRunStore store, RunEventWriter writer, Connection connection, Task stream, int chunks)
        {
            _store = store;
            _writer = writer;
            _connection = connection;
            _chunks = chunks;
            Stream = stream;
        }

        /// <summary>Gets the task of the loop that hands the chunks to the writer.</summary>
        public Task Stream { get; }

        /// <summary>Lets the connection answer, so that a stream a failed test left stopped at it can end.</summary>
        /// <returns>The completed task.</returns>
        public ValueTask DisposeAsync()
        {
            _connection.Release();

            return ValueTask.CompletedTask;
        }

        /// <summary>Gets how many writes the store was asked for since the connection was held, answered or not.</summary>
        public int Calls => _connection.Calls;

        /// <summary>Waits until a write is on the held connection, unanswered: the stream has met it.</summary>
        /// <returns>The completion task.</returns>
        public Task MeetsTheConnectionAsync()
            => WaitUntil.TrueAsync(() => _connection.Waiting > 0, "a write to be asked of the held connection");

        /// <summary>Lets the connection answer, ends the stream and the run, and reads back what the store holds.</summary>
        /// <returns>What the store holds.</returns>
        public async Task<Stored> ReleaseAsync()
        {
            _connection.Release();

            await Stream.ConfigureAwait(false);
            await _writer.CompleteAsync(RunStatus.Completed).ConfigureAwait(false);

            var sequences = new List<long>();
            var stored = 0;

            await foreach (var runEvent in _store.ReadEventsAsync(_writer.RunId))
            {
                sequences.Add(runEvent.Sequence);

                if (runEvent.Type == RunEventType.MessageDelta)
                {
                    stored++;
                }
            }

            return new Stored(_chunks, stored, sequences.SequenceEqual(Enumerable.Range(0, sequences.Count).Select(static n => (long)n)), _connection.Calls);
        }
    }

    /// <summary>What a store holds once a held stream is over.</summary>
    /// <param name="Chunks">How many chunks the stream handed over.</param>
    /// <param name="Count">How many chunks the store holds.</param>
    /// <param name="Contiguous">Whether the sequence numbers the store holds are 0 to n-1, each once.</param>
    /// <param name="Calls">How many writes the store was asked for, from the moment the connection was held to the end of the run.</param>
    public sealed record Stored(int Chunks, int Count, bool Contiguous, int Calls);

    /// <summary>A connection that answers each call at once, or not until it is released.</summary>
    internal sealed class Connection
    {
        private TaskCompletionSource _answer = Answered();
        private int _calls;
        private int _waiting;

        public int Calls => Volatile.Read(ref _calls);

        public int Waiting => Volatile.Read(ref _waiting);

        public void Hold()
        {
            _answer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _calls, 0);
        }

        public void Release() => _answer.TrySetResult();

        public ValueTask AnswerAsync()
        {
            var answer = _answer;

            Interlocked.Increment(ref _calls);

            if (answer.Task.IsCompleted)
            {
                return ValueTask.CompletedTask;
            }

            Interlocked.Increment(ref _waiting);

            return new ValueTask(answer.Task);
        }

        private static TaskCompletionSource Answered()
        {
            var answered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            answered.SetResult();

            return answered;
        }
    }

    /// <summary>Puts a round trip in front of every write of a run store.</summary>
    private sealed class LatentRunStore(IRunStore inner, Func<ValueTask> trip) : IRunStore
    {
        private ValueTask Trip() => trip();

        public async ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default)
        {
            await Trip().ConfigureAwait(false);

            return await inner.StartRunAsync(info, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
        {
            await Trip().ConfigureAwait(false);
            await inner.AppendEventAsync(runEvent, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask AppendEventsAsync(IReadOnlyList<RunEvent> events, CancellationToken cancellationToken = default)
        {
            await Trip().ConfigureAwait(false);
            await inner.AppendEventsAsync(events, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken cancellationToken = default)
        {
            await Trip().ConfigureAwait(false);
            await inner.CompleteRunAsync(completion, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask<long?> GetLastEventSequenceAsync(Guid runId, CancellationToken cancellationToken = default) => inner.GetLastEventSequenceAsync(runId, cancellationToken);

        public ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default) => inner.GetRunAsync(runId, cancellationToken);

        public ValueTask<IReadOnlyList<RunRecord>> QueryRunsAsync(RunQuery query, CancellationToken cancellationToken = default) => inner.QueryRunsAsync(query, cancellationToken);

        public ValueTask<RunStatistics> GetStatisticsAsync(RunStatisticsQuery query, CancellationToken cancellationToken = default) => inner.GetStatisticsAsync(query, cancellationToken);

        public IAsyncEnumerable<RunEvent> ReadEventsAsync(Guid runId, long fromSequence = 0, CancellationToken cancellationToken = default) => inner.ReadEventsAsync(runId, fromSequence, cancellationToken);

        public ValueTask RecordToolInvocationAsync(ToolInvocationRecord invocation, CancellationToken cancellationToken = default) => inner.RecordToolInvocationAsync(invocation, cancellationToken);

        public ValueTask<bool> CompleteLateToolInvocationAsync(LateToolCompletion completion, CancellationToken cancellationToken = default) => inner.CompleteLateToolInvocationAsync(completion, cancellationToken);

        public ValueTask<IReadOnlyList<ToolInvocationRecord>> ListToolInvocationsAsync(Guid runId, CancellationToken cancellationToken = default) => inner.ListToolInvocationsAsync(runId, cancellationToken);

        public ValueTask RecordNonTokenUsageAsync(NonTokenUsageRecord record, CancellationToken cancellationToken = default) => inner.RecordNonTokenUsageAsync(record, cancellationToken);

        public ValueTask<IReadOnlyList<NonTokenUsageRecord>> ListNonTokenUsageAsync(Guid runId, CancellationToken cancellationToken = default) => inner.ListNonTokenUsageAsync(runId, cancellationToken);

        public ValueTask<IReadOnlyList<ToolUsage>> GetToolUsageAsync(ToolUsageQuery query, CancellationToken cancellationToken = default) => inner.GetToolUsageAsync(query, cancellationToken);

        public ValueTask<IReadOnlyList<ExperimentVariantResult>> GetExperimentResultsAsync(ExperimentResultsQuery query, CancellationToken cancellationToken = default) => inner.GetExperimentResultsAsync(query, cancellationToken);

        public ValueTask<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(RunTimeSeriesQuery query, CancellationToken cancellationToken = default) => inner.GetTimeSeriesAsync(query, cancellationToken);

        public ValueTask UpdateRunCostAsync(Guid runId, RunCost? cost, string? tenantId, CancellationToken cancellationToken = default) => inner.UpdateRunCostAsync(runId, cost, tenantId, cancellationToken);

        public ValueTask<IReadOnlyList<RunCancellationRequest>> TouchHeartbeatAsync(IReadOnlyCollection<Guid> runIds, CancellationToken cancellationToken = default) => inner.TouchHeartbeatAsync(runIds, cancellationToken);

        public ValueTask<bool> RequestCancellationAsync(Guid runId, string tenantId, DateTimeOffset at, CancellationToken cancellationToken = default)
            => inner.RequestCancellationAsync(runId, tenantId, at, cancellationToken);

        public ValueTask<IReadOnlyList<RunRecord>> ClaimOrphanedRunsAsync(TimeSpan staleAfter, int max, CancellationToken cancellationToken = default) => inner.ClaimOrphanedRunsAsync(staleAfter, max, cancellationToken);
    }
}
