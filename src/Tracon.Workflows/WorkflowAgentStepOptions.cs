namespace Tracon;

/// <summary>
/// The settings of one agent step in a workflow graph built in code, passed to
/// <see cref="WorkflowAgentBinding.GetWorkflowAgentStep(System.IServiceProvider, string, string, Action{WorkflowAgentStepOptions})"/>.
/// </summary>
/// <remarks>
/// A stored workflow carries the same values in a <see cref="WorkflowNodeReference"/>; a graph built in
/// code has no node reference, so it chooses them here. A callback with properties rather than optional
/// parameters, for the reason <see cref="WorkflowFunctionOptions"/> gives.
/// </remarks>
public sealed class WorkflowAgentStepOptions
{
    /// <summary>
    /// Gets or sets the longest time the step may wait for the agent. <see langword="null"/> (the default)
    /// uses <c>Tracon:AgentGraph:ChildDeadline</c>.
    /// </summary>
    /// <remarks>
    /// The same setting as <see cref="WorkflowNodeReference.Timeout"/> on an agent node: it replaces
    /// <c>ChildDeadline</c> for this step, and the hard cutoff is the timeout plus 30 seconds. When the time
    /// is up the run ends <c>Failed</c> with <see cref="TraconChildRunTimeoutException"/>. Must be from one
    /// millisecond to about 49.7 days less 30 seconds.
    /// </remarks>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Gets or sets the version of the stored agent the step runs. <see langword="null"/> (the default) runs
    /// the agent's current version on every call.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same setting as <see cref="WorkflowNodeReference.AgentVersion"/>. Versions are numbered from 1, and
    /// only an agent stored in the database keeps versions: an agent defined in code has none.
    /// </para>
    /// <para>
    /// <strong>A version that cannot be held fails the step, not the build.</strong> A stored workflow checks
    /// the pin before its first node runs; the factory of a graph built in code is synchronous and cannot read
    /// the catalog, so a missing version or an agent that keeps no history ends the run <c>Failed</c> at the
    /// step, by name, before its model call. Earlier steps have already run.
    /// </para>
    /// </remarks>
    public int? AgentVersion { get; set; }
}
