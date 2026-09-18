using System.Collections.Concurrent;

namespace Tracon;

/// <summary>
/// Carries a tool failure that <see cref="ExplainedFailureAIFunction"/> turned
/// into a result to <see cref="ToolInvocationTracker"/>, keyed by call identity.
/// </summary>
/// <remarks>
/// <para>
/// The model reads a failed tool call's explanation only when that explanation
/// arrives as the call's RESULT — Microsoft Agent Framework replaces a thrown
/// exception with a generic sentence. Returning the message therefore has a
/// cost: <c>FunctionResultContent.Exception</c> is then empty, and the run
/// record cannot tell the call apart from an ordinary success by inspecting it.
/// </para>
/// <para>
/// This is the same ambient-write/scoped-read pattern
/// <see cref="ToolAuthorizationAccumulator"/> uses for a denial, which returns
/// a normal result for the same reason. The exception itself is carried, not a
/// flag: the record needs its text, and a timeout has to stay recognizable as
/// a timeout rather than becoming an ordinary failure.
/// </para>
/// </remarks>
internal sealed class ToolExplainedFailureAccumulator
{
    private readonly ConcurrentDictionary<string, TraconException> _byCallId = new(StringComparer.Ordinal);

    /// <summary>Records the failure a call reported as its result.</summary>
    /// <param name="callId">The call identity produced by the model.</param>
    /// <param name="exception">The failure whose message became the result.</param>
    public void RecordFailure(string callId, TraconException exception)
        => _byCallId[callId] = exception;

    /// <summary>Takes the failure for a call and removes it from the dictionary.</summary>
    /// <param name="callId">The call identity.</param>
    /// <returns>The failure, or <see langword="null"/> when the call did not report one.</returns>
    public TraconException? TakeFailure(string callId)
        => _byCallId.TryRemove(callId, out var exception) ? exception : null;
}
