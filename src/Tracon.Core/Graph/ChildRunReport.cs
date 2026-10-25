using Microsoft.Agents.AI;

namespace Tracon;

/// <summary>
/// The options a caller hands <see cref="ChildAgentInvoker"/> to learn which child run its call opened.
/// </summary>
/// <remarks>
/// <para>
/// The invoker draws the child run's id itself and does not return it, and the answer of a call holds
/// no run. The loop's <c>agentReview</c> criterion has to name the reviewer's run in the audit trail,
/// so it passes one of these as the call's options and reads it back afterwards. It carries no model
/// option: the invoker builds the child's options from the run's scope, not from the caller's.
/// </para>
/// <para>
/// Both fields stay <see langword="null"/> when the call is refused before a run starts (the depth
/// limit, the tenant, the budget, the quota), and a caller must read them in that case too.
/// </para>
/// </remarks>
internal sealed class ChildRunReport : AgentRunOptions
{
    /// <summary>Gets the id of the child run the call opened, or <see langword="null"/> when it opened none.</summary>
    public Guid? RunId { get; private set; }

    /// <summary>Gets the definition version the child run recorded, or <see langword="null"/> when none was opened or the version is unknown.</summary>
    public int? AgentVersion { get; private set; }

    /// <summary>Writes what the invoker learned about the child run into <paramref name="options"/>, when they are a report.</summary>
    /// <param name="options">The options the call received.</param>
    /// <param name="runId">The id the invoker gave the child run.</param>
    /// <param name="agent">The agent the child run executes.</param>
    internal static void Fill(AgentRunOptions? options, Guid? runId, AIAgent agent)
    {
        if (options is not ChildRunReport report)
        {
            return;
        }

        report.RunId = runId;

        // The recording wrapper is the outermost decorator, so it is the agent itself or one of its inner agents.
        report.AgentVersion = agent.GetService<RunRecordingAgent>()?.AgentVersion;
    }
}
