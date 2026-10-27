using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Keeps the tool calls of one run as the tool chain saw them: which call started, when, and how it ended.
/// </summary>
/// <remarks>
/// <para>
/// The run recorder used to learn of a tool call from the messages of the model's answer. A run that does not
/// stream has those messages only when it succeeds, so a run that ended in an exception (a budget refusal at the
/// next model call, a provider outage, a cancellation) wrote no row and no event for the calls it had made, and a
/// run that succeeded could not tell how long a call took. The outermost layer of the tool chain
/// (<see cref="CallRecordingAIFunction"/>) notes each call here, in memory, the moment it starts and the moment it
/// ends; the recorder reads the ledger once, when the run closes. Nothing is stored before that: a process that dies
/// while the run is open leaves no row for the calls only this ledger knew.
/// </para>
/// <para>
/// The same ambient-write/scoped-read pattern as <see cref="ToolExplainedFailureAccumulator"/>: the ledger is made
/// with the run's scope, the chain writes through <see cref="TraconRunContext.Current"/> and never opens a scope of
/// its own, and the recorder reads from the scope it opened. A child run has a ledger of its own, because it has a
/// scope of its own.
/// </para>
/// <para>
/// <strong>A call is written once.</strong> The message path and the ledger both know a call, so whoever writes its
/// row first claims it (<see cref="Claim"/>), and the other leaves it alone. A run that answered wrote its rows from
/// its messages. When it does not stream, the row takes its duration and its time from the ledger; when it streams,
/// the tracker measures the duration itself, between the two contents of the call, and only claims. The close step
/// writes what the ledger still holds (<see cref="Unclaimed"/>) only for a run that failed or was cancelled, because
/// only such a run has no messages. In a run that answered, an unclaimed call is one that an earlier model link ran
/// and a later link had answered again from its result: no row is written for it, so the call is not shown twice.
/// </para>
/// <para>
/// Calls that run at the same time (<see cref="ToolLoopSettings.AllowConcurrentToolCalls"/>) write concurrently, so
/// every change happens under one lock. The ledger holds no limit on size: a run's tool calls are bounded by the
/// tool loop and the tree's budget, and the ledger lives as long as the run.
/// </para>
/// </remarks>
internal sealed class ToolCallLedger
{
    private readonly TimeProvider _timeProvider;

    // Locks on the list itself: net8.0 has no System.Threading.Lock, and a separate
    // object field would trigger MA0158 on net9.0+ (the ToolInputLedger pattern).
    private readonly List<Entry> _entries = [];

    /// <summary>Creates the ledger of one run.</summary>
    /// <param name="timeProvider">The run's time source, the one its events and its tool rows are stamped with.</param>
    public ToolCallLedger(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        _timeProvider = timeProvider;
    }

    /// <summary>Records that a call started.</summary>
    /// <param name="call">The model's call, as the tool-call loop handed it to the chain.</param>
    /// <returns>
    /// The entry to settle when the call ends, or <see langword="null"/> when a call with the same identity is
    /// already open: a layer further out owns it, and the tool chain was composed twice around one tool.
    /// </returns>
    public Entry? Begin(FunctionCallContent call)
    {
        ArgumentNullException.ThrowIfNull(call);

        lock (_entries)
        {
            if (_entries.Exists(entry => entry.IsOpen && string.Equals(entry.Call.CallId, call.CallId, StringComparison.Ordinal)))
            {
                return null;
            }

            var started = new Entry(call, _timeProvider.GetTimestamp(), _timeProvider.GetUtcNow());
            _entries.Add(started);

            return started;
        }
    }

    /// <summary>Records that a call ended with the result the chain handed back to the tool-call loop.</summary>
    /// <param name="entry">The entry <see cref="Begin"/> returned.</param>
    /// <param name="result">
    /// The result as the loop and the model read it: a denial, a refusal and an explained failure are results too.
    /// </param>
    public void Settle(Entry entry, object? result) => End(entry, result, failure: null);

    /// <summary>Records that a call ended with an exception that left the chain.</summary>
    /// <param name="entry">The entry <see cref="Begin"/> returned.</param>
    /// <param name="failure">The exception.</param>
    public void Fail(Entry entry, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        End(entry, result: null, failure);
    }

    /// <summary>Claims the oldest call with this identity that nobody has written yet.</summary>
    /// <param name="callId">The call identity.</param>
    /// <returns>
    /// What the ledger holds for the call, or <see langword="null"/> when the call did not pass through the tool
    /// chain (a hosted tool, a tool of the framework) or was claimed already.
    /// </returns>
    public ToolCallSnapshot? Claim(string callId)
    {
        lock (_entries)
        {
            var found = _entries.Find(entry => !entry.Claimed && string.Equals(entry.Call.CallId, callId, StringComparison.Ordinal));

            if (found is null)
            {
                return null;
            }

            found.Claimed = true;

            return Snapshot(found);
        }
    }

    /// <summary>Reads the calls nobody has written yet, in the order they started. Nothing is claimed.</summary>
    /// <returns>A snapshot.</returns>
    public IReadOnlyList<ToolCallSnapshot> Unclaimed()
    {
        lock (_entries)
        {
            return [.. _entries.Where(static entry => !entry.Claimed).Select(Snapshot)];
        }
    }

    private void End(Entry entry, object? result, Exception? failure)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_entries)
        {
            if (!entry.IsOpen)
            {
                return;
            }

            // The duration comes from the monotonic timestamp. The wall time of the end is a label for the record, and
            // a wall clock can be corrected backwards while a call runs: the end never reads earlier than the start,
            // so the run record never shows a call that ended before it began.
            var endedUtc = _timeProvider.GetUtcNow();

            entry.EndedAt = _timeProvider.GetTimestamp();
            entry.EndedAtUtc = endedUtc < entry.StartedAtUtc ? entry.StartedAtUtc : endedUtc;
            entry.Result = result;
            entry.Failure = failure;
        }
    }

    private ToolCallSnapshot Snapshot(Entry entry)
        => new(
            entry.Call,
            entry.StartedAt,
            entry.StartedAtUtc,
            entry.EndedAtUtc,
            entry.EndedAtUtc is null ? null : _timeProvider.GetElapsedTime(entry.StartedAt, entry.EndedAt),
            entry.Result,
            entry.Failure);

    /// <summary>One call of the ledger. Its state changes only under the ledger's lock.</summary>
    internal sealed class Entry(FunctionCallContent call, long startedAt, DateTimeOffset startedAtUtc)
    {
        public FunctionCallContent Call { get; } = call;

        public long StartedAt { get; } = startedAt;

        public DateTimeOffset StartedAtUtc { get; } = startedAtUtc;

        public long EndedAt { get; set; }

        public DateTimeOffset? EndedAtUtc { get; set; }

        public object? Result { get; set; }

        public Exception? Failure { get; set; }

        public bool Claimed { get; set; }

        /// <summary>Gets whether the call neither ended nor was written: it may still be running.</summary>
        public bool IsOpen => EndedAtUtc is null && !Claimed;
    }
}

/// <summary>A tool call as the <see cref="ToolCallLedger"/> held it when it was read.</summary>
/// <param name="Call">The model's call: its name, its identity and its arguments.</param>
/// <param name="StartedAt">The run clock's timestamp when the call started.</param>
/// <param name="StartedAtUtc">The wall-clock time the call started.</param>
/// <param name="EndedAtUtc">The wall-clock time the call ended, or <see langword="null"/> while it had not ended.</param>
/// <param name="Duration">How long the call took, or <see langword="null"/> while it had not ended.</param>
/// <param name="Result">The result the chain handed back, when the call ended with one.</param>
/// <param name="Failure">The exception that left the chain, when the call ended with one.</param>
internal sealed record ToolCallSnapshot(
    FunctionCallContent Call,
    long StartedAt,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    TimeSpan? Duration,
    object? Result,
    Exception? Failure)
{
    /// <summary>Gets whether the call ended: with a result or with an exception.</summary>
    public bool IsSettled => EndedAtUtc is not null;
}
