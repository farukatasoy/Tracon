using System.Text;

namespace AgentPrism;

/// <summary>Who spoke one segment of a live conversation.</summary>
internal enum LiveTranscriptRole
{
    /// <summary>The person talking to the live model.</summary>
    User = 0,

    /// <summary>The live model.</summary>
    Assistant = 1,
}

/// <summary>One timed segment of a live conversation's transcript.</summary>
/// <param name="Role">Who spoke.</param>
/// <param name="Text">What was said.</param>
/// <param name="StartMilliseconds">When the segment begins, from the session's start.</param>
/// <param name="EndMilliseconds">When the segment ends, from the session's start.</param>
internal readonly record struct LiveTranscriptEntry(
    LiveTranscriptRole Role,
    string Text,
    int StartMilliseconds,
    int EndMilliseconds);

/// <summary>
/// Accumulates a live conversation's transcript so a delegation can be cut out of it.
/// </summary>
/// <remarks>
/// <para>
/// The problem this solves: the provider's delegation event carries metadata but
/// <strong>no task text</strong>. It says only <em>when</em> in the conversation the
/// model handed work over. The ledger holds what was said, with the provider's own
/// timings, so that moment can be turned into a prompt.
/// </para>
/// <para>
/// The class is pure: no clock, no network, no I/O. The timings come from the
/// provider's events, not from a <c>TimeProvider</c> — the provider measures against
/// the media it is carrying and AgentPrism is not, so a local clock would drift
/// against the very offsets the cut is made with.
/// </para>
/// <para>
/// Every member locks on <c>_entries</c>. Two threads reach this object by design
/// and at the default settings: the sideband pump appends transcript deltas while a
/// delegation task cuts a prompt out of the same list. Unsynchronized, a concurrent
/// add and remove on the backing list can throw or silently hand back a torn cut.
/// </para>
/// <para>
/// The ledger is <strong>bounded</strong>. A long conversation drops its oldest
/// entries rather than growing without limit; that is where the "oversized input"
/// failure mode is closed.
/// </para>
/// </remarks>
internal sealed class LiveTranscriptLedger
{
    private readonly List<LiveTranscriptEntry> _entries = [];
    private readonly int _maxCharacters;
    private readonly StringBuilder _pending = new();

    private LiveTranscriptRole _pendingRole;
    private int _pendingStart;
    private int _pendingEnd;
    private int _characters;

    /// <summary>Creates a ledger.</summary>
    /// <param name="maxCharacters">How many characters of transcript to keep.</param>
    public LiveTranscriptLedger(int maxCharacters)
        => _maxCharacters = Math.Max(256, maxCharacters);

    /// <summary>Takes a snapshot of the entries kept, oldest first.</summary>
    /// <remarks>
    /// A copy, not the live list: a caller that enumerated the backing list would
    /// race the pump's next append.
    /// </remarks>
    public IReadOnlyList<LiveTranscriptEntry> Snapshot()
    {
        lock (_entries)
        {
            return [.. _entries];
        }
    }

    /// <summary>Adds one transcript delta.</summary>
    /// <param name="role">Who spoke.</param>
    /// <param name="delta">The text.</param>
    /// <param name="startMilliseconds">When the delta begins.</param>
    /// <param name="endMilliseconds">When the delta ends.</param>
    /// <remarks>
    /// Consecutive deltas from the same speaker are merged into one entry: the
    /// provider emits a delta every couple of hundred milliseconds, and one entry per
    /// word would make both the cut and the prompt unreadable. A change of speaker
    /// closes the entry being built.
    /// </remarks>
    public void Append(LiveTranscriptRole role, string? delta, int startMilliseconds, int endMilliseconds)
    {
        if (delta is not { Length: > 0 })
        {
            return;
        }

        lock (_entries)
        {
            if (_pending.Length > 0 && _pendingRole != role)
            {
                CommitCore();
            }

            if (_pending.Length == 0)
            {
                _pendingRole = role;
                _pendingStart = startMilliseconds;
            }

            _pending.Append(delta);
            _pendingEnd = Math.Max(_pendingEnd, endMilliseconds);
        }
    }

    /// <summary>Closes the entry currently being built, if any.</summary>
    public void Commit()
    {
        lock (_entries)
        {
            CommitCore();
        }
    }

    private void CommitCore()
    {
        if (_pending.Length == 0)
        {
            return;
        }

        var text = _pending.ToString().Trim();
        _pending.Clear();

        if (text.Length == 0)
        {
            return;
        }

        _entries.Add(new LiveTranscriptEntry(_pendingRole, text, _pendingStart, _pendingEnd));
        _characters += text.Length;

        Trim();
    }

    /// <summary>Returns the conversation up to the given point.</summary>
    /// <param name="offsetMilliseconds">
    /// The provider's delegation offset. Entries starting at or before it are kept,
    /// so the utterance the delegation was cut at is included.
    /// </param>
    /// <param name="maxEntries">How many of the newest matching entries to return.</param>
    /// <returns>The cut, oldest first; empty when nothing was said yet.</returns>
    public IReadOnlyList<LiveTranscriptEntry> Cut(int offsetMilliseconds, int maxEntries)
    {
        lock (_entries)
        {
            // 🚨 The entry still being built is committed first. A delegation is
            // created on the last word of an utterance, and the provider sends that
            // word's delta and the delegation event in the same instant — without
            // this the cut would routinely miss the sentence it exists to carry.
            CommitCore();

            if (maxEntries <= 0)
            {
                return [];
            }

            var matching = new List<LiveTranscriptEntry>();

            foreach (var entry in _entries)
            {
                if (entry.StartMilliseconds <= offsetMilliseconds)
                {
                    matching.Add(entry);
                }
            }

            return matching.Count <= maxEntries
                ? matching
                : matching.GetRange(matching.Count - maxEntries, maxEntries);
        }
    }

    /// <summary>Renders a cut as the prompt of a delegated run.</summary>
    /// <param name="cut">The cut.</param>
    /// <returns>The prompt.</returns>
    /// <remarks>
    /// The rendering is stored verbatim by <c>IRunInputStore</c>, so the run stays
    /// replayable: what the agent saw is exactly what is on record.
    /// </remarks>
    public static string RenderPrompt(IReadOnlyList<LiveTranscriptEntry> cut)
    {
        ArgumentNullException.ThrowIfNull(cut);

        var builder = new StringBuilder();

        builder.Append(
            "The following is a live voice conversation. Carry out the task the user is asking for " +
            "and answer with the result only, in one or two sentences, so it can be spoken aloud.");
        builder.AppendLine();
        builder.AppendLine();

        foreach (var entry in cut)
        {
            builder.Append(entry.Role == LiveTranscriptRole.User ? "User: " : "Assistant: ");
            builder.AppendLine(entry.Text);
        }

        return builder.ToString();
    }

    private void Trim()
    {
        while (_characters > _maxCharacters && _entries.Count > 1)
        {
            _characters -= _entries[0].Text.Length;
            _entries.RemoveAt(0);
        }
    }
}
