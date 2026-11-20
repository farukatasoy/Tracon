using Microsoft.Agents.AI;

namespace Tracon;

/// <summary>
/// Marks an agent the catalog compiled for one set of parameter values and carries that set, so the
/// run recording wrapper can write it to the run record.
/// </summary>
/// <remarks>
/// <para>
/// An in-process caller resolves the agent through <see cref="IAgentCatalog.ResolveAsync"/> with
/// <see cref="AgentResolveOptions.Parameters"/> and runs it with a plain <c>RunAsync</c>. It has no way
/// to fill <see cref="TraconRunOptions"/>'s internal value record, and a run that recorded no values
/// cannot be replayed. The marker is the other carrier: it travels with the agent, below every
/// decorator, and the recording wrapper reads it through <c>GetService</c>.
/// </para>
/// <para>
/// Internal callers (the queue, a batch item, an eval case, a replay, a continuation, an approval
/// resume, a workflow step) put no marker on their agent. They hand the same set to the run options,
/// and <see cref="TraconRunOptions.ParameterValues"/> wins when both exist. Two carriers can never
/// disagree for long, because both hold the set a single <c>ParameterValueSet.Resolve</c> produced.
/// </para>
/// <para>
/// Same shape as <see cref="UnboundParameterGuard"/>: a pass-through <see cref="DelegatingAIAgent"/> that
/// the recording wrapper finds from outside, because every decorator forwards <c>GetService</c>.
/// </para>
/// </remarks>
internal sealed class BoundParameterValues : DelegatingAIAgent
{
    /// <summary>Creates the marker.</summary>
    /// <param name="innerAgent">The agent compiled for <paramref name="values"/>.</param>
    /// <param name="values">The final value set, defaults included.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public BoundParameterValues(AIAgent innerAgent, IReadOnlyDictionary<string, string> values)
        : base(innerAgent)
    {
        ArgumentNullException.ThrowIfNull(values);

        Values = values;
    }

    /// <summary>Gets the final value set the agent was compiled with, defaults included.</summary>
    public IReadOnlyDictionary<string, string> Values { get; }
}
