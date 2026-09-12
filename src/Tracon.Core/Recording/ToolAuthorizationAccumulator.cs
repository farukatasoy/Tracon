using System.Collections.Concurrent;

namespace Tracon;

/// <summary>
/// Carries the authorization decision made by <see cref="AuthorizingAIFunction"/>
/// within a run to <see cref="ToolInvocationTracker"/>, keyed by call identity.
/// </summary>
/// <remarks>
/// A denied call returns a normal (non-exceptional) result to the model, so
/// <see cref="ToolInvocationTracker"/> cannot tell a denial apart from an
/// ordinary successful result by inspecting <c>FunctionResultContent</c>
/// alone. This is the same ambient-write/scoped-read pattern
/// <see cref="ToolUsageAccumulator"/> uses for non-token usage,
/// applied to an authorization decision instead of a metric.
/// </remarks>
internal sealed class ToolAuthorizationAccumulator
{
    private readonly ConcurrentDictionary<string, bool> _deniedByCallId = new(StringComparer.Ordinal);

    /// <summary>Records that a call was denied.</summary>
    /// <param name="callId">The call identity produced by the model.</param>
    public void RecordDenied(string callId) => _deniedByCallId[callId] = true;

    /// <summary>Takes the denial marker for a call and removes it from the dictionary.</summary>
    /// <param name="callId">The call identity.</param>
    /// <returns><see langword="true"/> if the call was denied.</returns>
    public bool TakeDenied(string callId) => _deniedByCallId.TryRemove(callId, out _);
}
