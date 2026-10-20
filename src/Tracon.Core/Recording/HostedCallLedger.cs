using System.Collections.Concurrent;

namespace Tracon;

/// <summary>
/// Carries the registered name of each hosted tool call of a run, from the cost ring that reads the
/// model call's tools to the run recorder, keyed by call identity.
/// </summary>
/// <remarks>
/// <para>
/// A provider's response names a kind of call and its identifier, not the tool Tracon registered.
/// The ring is the one place that holds the options of the model call, so it learns the name
/// (<see cref="HostedToolIdentity"/>) and writes it here; the recorder reads it when the same content
/// reaches it and writes the call as that tool's. A call with no entry belongs to a tool Tracon did not
/// register (the harness's web search) and stays a content event.
/// </para>
/// <para>
/// The same ambient-write/scoped-read pattern as <see cref="ToolUsageAccumulator"/>. The object lives as
/// long as the run, so the dictionary needs no eviction.
/// </para>
/// </remarks>
internal sealed class HostedCallLedger
{
    private readonly ConcurrentDictionary<string, string> _names = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _written = new(StringComparer.Ordinal);

    /// <summary>Records the registered name of a call.</summary>
    /// <param name="callId">The call identity the provider assigned.</param>
    /// <param name="toolName">The registered tool name.</param>
    public void Record(string callId, string toolName) => _names[callId] = toolName;

    /// <summary>Claims the right to write a call's events, once.</summary>
    /// <param name="callId">The call identity.</param>
    /// <returns><see langword="true"/> for the first claim of the call, <see langword="false"/> after it.</returns>
    /// <remarks>
    /// A streamed call is not one content: OpenAI shows a web search when it starts and again when it ends, and a
    /// code interpreter once for every fragment of its code. The same
    /// call identifier comes back; the call is one call.
    /// </remarks>
    public bool TryClaimWrite(string callId) => _written.TryAdd(callId, true);

    /// <summary>Reads the registered name of a call.</summary>
    /// <param name="callId">The call identity.</param>
    /// <param name="toolName">The registered tool name.</param>
    /// <returns><see langword="true"/> when the call belongs to a registered hosted tool.</returns>
    public bool TryGetName(string callId, out string toolName)
    {
        if (_names.TryGetValue(callId, out var name))
        {
            toolName = name;

            return true;
        }

        toolName = string.Empty;

        return false;
    }
}
