using System.Collections.Concurrent;

namespace Tracon.Samples.MafFeatures;

/// <summary>Collects the model calls the ring of <see cref="ModelCallLogExtensionRegistration"/> sees.</summary>
public sealed class ModelCallLog
{
    private readonly ConcurrentQueue<string> _calls = new();

    /// <summary>The calls so far, oldest first, as <c>agent:role</c>.</summary>
    /// <remarks>The role is the role of the last message the model receives: <c>user</c> for a question, <c>tool</c> for a tool result.</remarks>
    public IReadOnlyList<string> Calls => [.. _calls];

    /// <summary>Records one model call.</summary>
    /// <param name="agent">The agent that made the call.</param>
    /// <param name="role">The role of the last message the model receives.</param>
    public void Record(string agent, string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        _calls.Enqueue($"{agent}:{role}");
    }
}
