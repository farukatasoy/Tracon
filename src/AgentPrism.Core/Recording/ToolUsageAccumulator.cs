using System.Collections.Concurrent;

namespace AgentPrism;

/// <summary>
/// Carries the non-token metrics reported by tools within a run to
/// <see cref="ToolInvocationTracker"/>, keyed by call identity.
/// </summary>
/// <remarks>
/// <para>
/// A tool runs in an <c>AIFunction</c> body and cannot write its own record;
/// the record is produced from the <c>ToolInvoking</c>/<c>ToolInvoked</c>
/// event pair. The single key that ties a reported metric to the correct
/// record is the <strong>call identity</strong>.
/// </para>
/// <para>
/// The dictionary is concurrent: when
/// <c>FunctionInvokingChatClient.AllowConcurrentInvocation</c> is enabled,
/// multiple tools run at the same time within the same run.
/// </para>
/// <para>
/// A metric that was reported but never matched to a result (the call was
/// canceled, the result never arrived) stays in the dictionary and is
/// discarded along with the run. This is not a leak: the object's lifetime
/// is the run's lifetime.
/// </para>
/// </remarks>
internal sealed class ToolUsageAccumulator
{
    private readonly ConcurrentDictionary<string, ToolCallUsage> _byCallId = new(StringComparer.Ordinal);

    /// <summary>Records the metric for a call.</summary>
    /// <param name="callId">The call identity produced by the model.</param>
    /// <param name="usage">The metric.</param>
    /// <remarks>
    /// If the same identity is reported a second time, the last value wins:
    /// if a tool makes multiple provider calls internally, it reports the
    /// total at the end.
    /// </remarks>
    public void Report(string callId, ToolCallUsage usage) => _byCallId[callId] = usage;

    /// <summary>Takes the metric for a call and removes it from the dictionary.</summary>
    /// <param name="callId">The call identity.</param>
    /// <returns>The reported metric; <see langword="null"/> if none.</returns>
    public ToolCallUsage? Take(string callId)
        => _byCallId.TryRemove(callId, out var usage) ? usage : null;
}
