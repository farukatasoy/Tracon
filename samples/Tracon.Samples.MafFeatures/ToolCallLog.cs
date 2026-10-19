using System.Collections.Concurrent;

namespace Tracon.Samples.MafFeatures;

/// <summary>Collects the tool calls <see cref="ToolCallLogDecorator"/> sees.</summary>
public sealed class ToolCallLog
{
    private readonly ConcurrentQueue<string> _calls = new();

    /// <summary>The calls so far, oldest first, as <c>agent:tool</c>.</summary>
    public IReadOnlyList<string> Calls => [.. _calls];

    /// <summary>Records one tool call.</summary>
    /// <param name="agent">The agent that made the call.</param>
    /// <param name="tool">The tool it called.</param>
    public void Record(string agent, string tool)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(tool);

        _calls.Enqueue($"{agent}:{tool}");
    }
}
