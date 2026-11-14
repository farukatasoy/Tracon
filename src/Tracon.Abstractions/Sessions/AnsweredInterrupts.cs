using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// The requests of a session that a request has answered but whose answer the session's history does not hold yet:
/// tool approvals and tool inputs that were applied by a run that has not saved its session.
/// </summary>
/// <remarks>
/// <para>
/// A run saves its session when it ends. An approval that a request applied is therefore pending in the stored history
/// until then, and a run whose client left (the stream was closed, the run was cancelled) never gets there: the same approval
/// sent again reads as pending and runs the tool a second time. The request that applies an answer writes its identity here,
/// in a second save made right after the answer is built and before the run starts, and every reader of what is pending
/// (the approval resolver, the tool input resolver, the reader that tells an AG-UI front end what it may answer, and
/// the mailbox that resumes a queued run) leaves these identities out.
/// </para>
/// <para>
/// The identities are held as one text, one per line, so that they are written and read without a serializer that depends on
/// reflection; an identity that holds a line break is not held (the answer then behaves as it did before this existed).
/// An identity leaves the set when the stored history answers it, so the set never outgrows the answers a session still
/// waits to see saved, and it is bounded.
/// </para>
/// </remarks>
internal static class AnsweredInterrupts
{
    /// <summary>The session state key the answered identities are kept under.</summary>
    internal const string Key = "Tracon.AnsweredInterrupts";

    /// <summary>The most identities held: the oldest are dropped first.</summary>
    internal const int MaxIdentities = 256;

    private const char Separator = '\n';

    /// <summary>Reads the identities of the interruptions the session has answered and not yet saved.</summary>
    /// <param name="session">The session.</param>
    /// <returns>The identities; empty when there are none.</returns>
    public static HashSet<string> Read(AgentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new HashSet<string>(Parse(session), StringComparer.Ordinal);
    }

    /// <summary>Adds the identities a request has answered and drops those that the stored history now answers.</summary>
    /// <param name="session">The session.</param>
    /// <param name="answered">The identities of the answers the request applied.</param>
    /// <param name="answeredInHistory">The identities the stored history already holds an answer for.</param>
    /// <returns><see langword="true"/> when the set changed and the session has to be saved.</returns>
    public static bool Write(AgentSession session, IEnumerable<string> answered, IReadOnlySet<string> answeredInHistory)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(answered);
        ArgumentNullException.ThrowIfNull(answeredInHistory);

        var before = Parse(session);
        var held = before.Where(id => !answeredInHistory.Contains(id)).ToList();

        foreach (var id in answered)
        {
            if (id.Length > 0 && !id.Contains(Separator, StringComparison.Ordinal) && !held.Contains(id, StringComparer.Ordinal))
            {
                held.Add(id);
            }
        }

        if (held.Count > MaxIdentities)
        {
            held.RemoveRange(0, held.Count - MaxIdentities);
        }

        if (held.SequenceEqual(before, StringComparer.Ordinal))
        {
            return false;
        }

        if (held.Count == 0)
        {
            session.StateBag.TryRemoveValue(Key);
        }
        else
        {
            session.StateBag.SetValue(Key, string.Join(Separator, held), AIJsonUtilities.DefaultOptions);
        }

        return true;
    }

    private static List<string> Parse(AgentSession session)
        => session.StateBag.TryGetValue<string>(Key, out var text, AIJsonUtilities.DefaultOptions) && !string.IsNullOrEmpty(text)
            ? [.. text.Split(Separator, StringSplitOptions.RemoveEmptyEntries)]
            : [];
}
