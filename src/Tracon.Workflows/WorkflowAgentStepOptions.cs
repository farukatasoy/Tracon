namespace Tracon;

/// <summary>
/// The settings of one agent step in a workflow graph built in code, passed to
/// <see cref="WorkflowAgentBinding.GetWorkflowAgentStep(System.IServiceProvider, string, string, Action{WorkflowAgentStepOptions})"/>.
/// </summary>
/// <remarks>
/// A stored workflow carries <see cref="Timeout"/> and <see cref="AgentVersion"/> in a
/// <see cref="WorkflowNodeReference"/>; a graph built in code has no node reference, so it chooses them here.
/// <see cref="AgentVersionParameter"/> has no node-reference twin: a stored workflow is pinned as a whole by
/// the definition version a run starts with. A callback with properties rather than optional parameters, for
/// the reason <see cref="WorkflowFunctionOptions"/> gives.
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
    /// only an agent stored in the database keeps versions: an agent defined in code has none. Cannot be set
    /// together with <see cref="AgentVersionParameter"/>.
    /// </para>
    /// <para>
    /// <strong>A version that cannot be held fails the step, not the build.</strong> A stored workflow checks
    /// the pin before its first node runs; the factory of a graph built in code is synchronous and cannot read
    /// the catalog, so a missing version or an agent that keeps no history ends the run <c>Failed</c> at the
    /// step, by name, before its model call. Earlier steps have already run.
    /// </para>
    /// </remarks>
    public int? AgentVersion { get; set; }

    /// <summary>
    /// Gets or sets the name of a run parameter that picks the version of the stored agent the step runs.
    /// <see langword="null"/> (the default) leaves the choice to <see cref="AgentVersion"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The value is read every time the step runs</strong>, from the run's parameter values, so one graph
    /// serves every version: the factory of a graph built in code runs before a run's parameters are bound and
    /// cannot read them. Declare the parameter in <see cref="WorkflowRegistrationOptions.Parameters"/>. A run
    /// that leaves it empty or out runs the agent's current version; a whole number from 1 runs that version,
    /// and the step's run row records it. Declare the parameter optional, with no default, when a run may
    /// leave it out: a required one is refused at the start of a run that omits it.
    /// </para>
    /// <para>
    /// A run that continues after a human answer reads the value its first part started with. A number keeps
    /// that version through the wait; an empty value still means the current version, resolved when the step
    /// runs, so an edit made while the run waited is the version a later step runs.
    /// </para>
    /// <para>
    /// <strong>A value that cannot be held fails the step, not the build.</strong> A value that is not a whole
    /// number from 1, a name the workflow does not declare, a version the agent does not have and an agent that
    /// keeps no history all end the run <c>Failed</c> at the step, by name, before its model call. The error
    /// for a value that is not a number, and for a name the workflow does not declare, names the parameter and
    /// never repeats the value; a version the agent does not have is named by its number, and an agent that
    /// keeps no history says so. Earlier steps have already run. Cannot be set together with
    /// <see cref="AgentVersion"/>: the graph is refused when it is built.
    /// </para>
    /// <para>
    /// Whoever may start the run chooses the version, and any stored version of the agent can be chosen. Pass
    /// the value from your own code, not from an end user's input, unless every version is meant to be reachable.
    /// </para>
    /// <example>
    /// <code>
    /// tracon.AddWorkflow(
    ///     "episode",
    ///     services =>
    ///     {
    ///         var writer = services.GetWorkflowAgentStep("episode", "writer", step => step.AgentVersionParameter = "writerVersion");
    ///
    ///         return new WorkflowBuilder(writer).WithOutputFrom(writer).WithName("episode").Build();
    ///     },
    ///     options => options.Parameters =
    ///     [
    ///         new AgentParameter { Name = "writerVersion", Kind = AgentParameterKind.Number },
    ///     ]);
    /// </code>
    /// </example>
    /// </remarks>
    public string? AgentVersionParameter { get; set; }
}
