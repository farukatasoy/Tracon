using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Holds the source run's recorded tool results in a ledger and replays them
/// in <see cref="ReplayToolMode.ReplayTools"/> mode.
/// </summary>
/// <remarks>
/// <para>
/// Matching is done by the <c>(tool name, arguments)</c> pair.
/// <c>tool_call_id</c> is <strong>not used</strong>: in the new run the model
/// produces new call identities that never overlap with the old ones.
/// </para>
/// <para>
/// Arguments are compared in the format they were recorded in
/// (<c>RunRecordingAgent.FormatArguments</c>: <c>"name=value, name2=value2"</c>).
/// If the same tool was called multiple times with the same arguments,
/// results are consumed <strong>in recording order</strong>; this keeps the
/// "ask the same question twice, get two different answers" scenario faithful.
/// </para>
/// <para>
/// With <c>recordLiveCalls</c> (constructor) set, the KEY SET is
/// <strong>not</strong> fixed at construction: a call that runs live (no
/// match found, <see cref="ToolPlaybackMismatchPolicy.RunLive"/>) writes its
/// own outcome back into the ledger under its own key. This is how
/// <see cref="FallbackChatClient"/> shares one ledger across every link of a
/// fallback chain — the first link to actually run a tool records it, and
/// the next link to ask the same question is answered from here instead of
/// running the tool's body a second time.
/// </para>
/// <para>
/// <strong>A wrapper never reads back its own write.</strong> Each call to
/// <see cref="Wrap"/> produces one <see cref="PlaybackFunction"/> instance,
/// and that instance's identity is stamped on every entry it live-records
/// (<see cref="LedgerEntry.Owner"/>). <see cref="Take"/> only matches an
/// entry whose owner is a DIFFERENT instance (or an entry with no owner —
/// the historical, database-sourced case). Without this, a single link's own
/// tool-call turn asking the same question twice — with no fallback ever
/// triggered — would silently answer the second ask from the ledger instead
/// of running the tool a second time, exactly as if it were a genuine
/// cross-link replay; the two are not the same event and must not look alike
/// to whichever link is asking.
/// </para>
/// </remarks>
internal sealed class RecordedToolPlayback
{
    // With recordLiveCalls, a live call can add a KEY that was not present at
    // construction (from a concurrent tool call, or a later fallback link) —
    // ConcurrentDictionary, not Dictionary. The queue itself still needs no
    // lock for concurrent calls under the SAME key — a lock would require a
    // different type across the targeted three frameworks (net8/9/10).
    private readonly ConcurrentDictionary<string, ConcurrentQueue<LedgerEntry>> _byKey;
    private readonly ToolPlaybackMismatchPolicy _mismatchPolicy;
    private readonly bool _recordLiveCalls;

    /// <summary>Creates a new ledger from recorded calls.</summary>
    /// <param name="invocations">The source run's tool calls (in chronological order).</param>
    /// <param name="mismatchPolicy">
    /// What an unmatched call does. Defaults to <see cref="ToolPlaybackMismatchPolicy.Stop"/>
    /// — replay's own behavior.
    /// </param>
    /// <param name="recordLiveCalls">
    /// Whether a call that runs live (see <paramref name="mismatchPolicy"/>)
    /// writes its own result or error back into this ledger. Defaults to
    /// <see langword="false"/>: replay and continuation keep today's behavior
    /// unchanged — only <see cref="FallbackChatClient"/> turns this on.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="invocations"/> is <see langword="null"/>.</exception>
    public RecordedToolPlayback(
        IReadOnlyList<ToolInvocationRecord> invocations,
        ToolPlaybackMismatchPolicy mismatchPolicy = ToolPlaybackMismatchPolicy.Stop,
        bool recordLiveCalls = false)
    {
        ArgumentNullException.ThrowIfNull(invocations);

        _byKey = new ConcurrentDictionary<string, ConcurrentQueue<LedgerEntry>>(StringComparer.Ordinal);
        _mismatchPolicy = mismatchPolicy;
        _recordLiveCalls = recordLiveCalls;

        foreach (var invocation in invocations)
        {
            var key = CreateKey(invocation.ToolName, invocation.Arguments);

            // No owner: a historical, database-sourced call is never a
            // link's own write, so it is always eligible to be taken.
            _byKey.GetOrAdd(key, static _ => new ConcurrentQueue<LedgerEntry>()).Enqueue(new LedgerEntry(invocation, Owner: null));
        }
    }

    /// <summary>Gets the number of recorded calls this ledger carries.</summary>
    public int RecordedCallCount => _byKey.Values.Sum(static queue => queue.Count);

    /// <summary>
    /// Gets the first unmatched call. <see langword="null"/> if no mismatch occurred.
    /// </summary>
    public ReplayToolMismatchException? Mismatch => Volatile.Read(ref _mismatch);

    private ReplayToolMismatchException? _mismatch;

    /// <summary>Throws the unmatched call, if any occurred.</summary>
    /// <exception cref="ReplayToolMismatchException">An unmatched call occurred.</exception>
    public void ThrowIfMismatched()
    {
        if (Mismatch is { } mismatch)
        {
            throw mismatch;
        }
    }

    /// <summary>Wraps a tool with a player that never runs its body.</summary>
    /// <param name="function">The tool to wrap.</param>
    /// <returns>The player.</returns>
    public AIFunction Wrap(AIFunction function) => new PlaybackFunction(function, this);

    /// <summary>
    /// Consumes the recorded result; if none is found, applies
    /// <see cref="ToolPlaybackMismatchPolicy"/>.
    /// </summary>
    /// <param name="toolName">The tool being called.</param>
    /// <param name="arguments">The call's arguments (in the recorded format).</param>
    /// <param name="asker">
    /// The identity of the <see cref="PlaybackFunction"/> making this call.
    /// An entry owned by <paramref name="asker"/> itself is never matched —
    /// see the class remarks.
    /// </param>
    /// <returns>The recorded call; <see langword="null"/> if no match.</returns>
    /// <remarks>
    /// <para>
    /// Under <see cref="ToolPlaybackMismatchPolicy.Stop"/> (replay), <strong>no
    /// exception is THROWN here, and this is the result of measurement.</strong>
    /// <c>FunctionInvokingChatClient</c> CATCHES an exception coming out of a
    /// tool body, turns the error into a tool result, and continues the loop;
    /// this was measured: a thrown <c>ReplayToolMismatchException</c> never
    /// reached the endpoint and the request returned <c>200</c>. Instead, the
    /// mismatch is recorded, the loop is cut with <c>FunctionInvocationContext.Terminate</c>,
    /// and after the run ends it is thrown by <c>ReplayMismatchGuard</c> via
    /// <see cref="ThrowIfMismatched"/> — at that point the exception escapes
    /// the recording wrapper, the <c>runs</c> row closes as <c>Failed</c>,
    /// and the endpoint returns <c>422</c>.
    /// </para>
    /// <para>
    /// Under <see cref="ToolPlaybackMismatchPolicy.RunLive"/> (continuation),
    /// NEITHER of those happen: <see cref="Mismatch"/> stays <see langword="null"/>
    /// and the loop is not cut. An unmatched call at this policy is not a
    /// failure — every call past the interruption point is, by definition,
    /// unrecorded. The caller (<see cref="PlaybackFunction"/>) runs the
    /// tool's real body instead.
    /// </para>
    /// </remarks>
    private ToolInvocationRecord? Take(string toolName, string? arguments, object? asker)
    {
        var key = CreateKey(toolName, arguments);

        if (_byKey.TryGetValue(key, out var queue))
        {
            // Self-owned entries only ever sit at the TAIL: a link finishes
            // its entire call (including every internal turn) before the
            // next link starts, so nothing written by an EARLIER link can
            // ever appear after this asker's own writes. The moment the
            // front is self-owned, everything behind it is too — there is
            // nothing left to borrow.
            while (queue.TryPeek(out var candidate) && !ReferenceEquals(candidate.Owner, asker))
            {
                if (!queue.TryDequeue(out var dequeued))
                {
                    continue;
                }

                if (!ReferenceEquals(dequeued.Owner, asker))
                {
                    return dequeued.Record;
                }

                // A concurrent dequeue elsewhere took the entry this peek
                // saw; what came out instead is this asker's OWN entry.
                // Put it back — it still belongs at/after the tail — and
                // keep looking.
                queue.Enqueue(dequeued);
            }
        }

        if (_mismatchPolicy == ToolPlaybackMismatchPolicy.Stop)
        {
            Interlocked.CompareExchange(ref _mismatch, ReplayToolMismatchException.For(toolName, arguments), null);

            if (FunctionInvokingChatClient.CurrentContext is { } context)
            {
                context.Terminate = true;
            }
        }

        return null;
    }

    /// <summary>
    /// Writes a live call's own outcome back into the ledger, when
    /// <c>recordLiveCalls</c> was enabled at construction.
    /// </summary>
    /// <remarks>
    /// <paramref name="owner"/> is the identity of the
    /// <see cref="PlaybackFunction"/> that ran this call; <see cref="Take"/>
    /// never matches this entry back to that same owner (see the class
    /// remarks). <paramref name="error"/> must already be redacted
    /// (<see cref="ToolFailureText.Get"/>) before it reaches here — the same
    /// rule <c>ToolInvocationTracker.OnResult</c> applies for a call recorded
    /// to a store. This ledger is never written to a store itself, but a
    /// matched error is re-thrown as a <see cref="TraconException"/>
    /// message (<see cref="PlaybackFunction.InvokeCoreAsync"/>) that a normal
    /// run then DOES record for real; an unredacted raw exception message
    /// would reach that path wrapped in a type <c>ToolFailureText.Get</c>
    /// treats as already-safe.
    /// </remarks>
    private void RecordLiveResult(string toolName, string? arguments, object? result, string? error, object owner)
    {
        var key = CreateKey(toolName, arguments);

        var record = new ToolInvocationRecord
        {
            Id = TraconId.NewId(),
            RunId = Guid.Empty,
            ToolName = toolName,
            Arguments = arguments,
            Result = error is null && ToolResultText.TryGetText(result, out var text) ? text : null,
            Error = error,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _byKey.GetOrAdd(key, static _ => new ConcurrentQueue<LedgerEntry>()).Enqueue(new LedgerEntry(record, owner));
    }

    /// <summary>One queued ledger entry and, for a live-recorded one, who wrote it.</summary>
    /// <param name="Record">The call's outcome.</param>
    /// <param name="Owner">
    /// The <see cref="PlaybackFunction"/> that live-recorded this entry, or
    /// <see langword="null"/> for a historical, database-sourced one (always
    /// eligible to be taken, regardless of asker).
    /// </param>
    private readonly record struct LedgerEntry(ToolInvocationRecord Record, object? Owner);

    private static string CreateKey(string toolName, string? arguments)
        => string.Concat(toolName, " ", arguments ?? string.Empty);

    /// <summary>
    /// Converts the call arguments to the format used when they were recorded.
    /// </summary>
    /// <remarks>
    /// The format MUST be <strong>identical</strong> to
    /// <c>RunRecordingAgent.FormatArguments</c>; otherwise no call matches
    /// and every replay stops with <c>422</c>. Both places use manual,
    /// reflection-free formatting (AOT).
    /// </remarks>
    private static string? FormatArguments(AIFunctionArguments arguments)
        => arguments.Count == 0
            ? null
            : string.Join(", ", arguments.Select(static pair => $"{pair.Key}={pair.Value}"));

    /// <summary>A player that never runs the wrapped tool's body.</summary>
    /// <remarks>
    /// <see cref="DelegatingAIFunction"/> carries the name, description, and
    /// JSON schema through unchanged; the model does not see the tool as changed.
    /// </remarks>
    private sealed class PlaybackFunction(AIFunction inner, RecordedToolPlayback playback)
        : DelegatingAIFunction(inner)
    {
        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(arguments);

            var formatted = FormatArguments(arguments);
            var record = playback.Take(Name, formatted, asker: this);

            if (record is null)
            {
                if (playback._mismatchPolicy == ToolPlaybackMismatchPolicy.RunLive)
                {
                    // Past the interruption point: this call has no recorded
                    // result because it never ran. The wrapped tool's own
                    // body runs for real, exactly as if playback did not exist.
                    return playback._recordLiveCalls
                        ? RunLiveAndRecordAsync(arguments, formatted, cancellationToken)
                        : base.InvokeCoreAsync(arguments, cancellationToken);
                }

                // The loop was already cut; the returned text is only a
                // marker visible in the stream. The actual outcome is
                // produced by ThrowIfMismatched.
                return new ValueTask<object?>(
                    $"[Tracon] Replay stopped: no recorded result for call '{Name}'.");
            }

            // If the recorded call failed, the error is replayed too: a
            // faithful repeat must not show a failed tool as successful.
            if (record.Error is { } error)
            {
                throw new TraconException(
                    $"The recorded '{record.ToolName}' call failed in the source run: {error}");
            }

            return new ValueTask<object?>(record.Result);
        }

        /// <summary>
        /// Runs the wrapped tool's real body and writes its outcome back into
        /// the ledger, so the NEXT link to ask this exact question is
        /// answered from here instead of running the body again.
        /// </summary>
        private async ValueTask<object?> RunLiveAndRecordAsync(
            AIFunctionArguments arguments, string? formattedArguments, CancellationToken cancellationToken)
        {
            object? result;

            try
            {
                result = await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A cancellation is not recorded: it carries no durable
                // outcome, and the caller (FallbackChatClient) never tries a
                // next link after one — there is nothing for a future link to
                // reuse.
                playback.RecordLiveResult(Name, formattedArguments, result: null, error: ToolFailureText.Get(exception), owner: this);
                throw;
            }

            playback.RecordLiveResult(Name, formattedArguments, result, error: null, owner: this);
            return result;
        }
    }
}

/// <summary>What <see cref="RecordedToolPlayback"/> does with a call it has no recorded result for.</summary>
internal enum ToolPlaybackMismatchPolicy
{
    /// <summary>Replay's own behavior: cut the tool-calling loop; the run ends as a mismatch.</summary>
    Stop = 0,

    /// <summary>
    /// Continuation's behavior: run the tool's real body. Used only past a
    /// run's interruption point, where a recorded result can never exist
    /// because the call never happened.
    /// </summary>
    RunLive = 1,
}
