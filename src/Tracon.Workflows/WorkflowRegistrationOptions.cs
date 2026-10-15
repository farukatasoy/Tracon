namespace Tracon;

/// <summary>
/// The settings of a workflow defined in code, passed to
/// <see cref="TraconWorkflowsBuilderExtensions.AddWorkflow(ITraconBuilder, string, Func{IServiceProvider, Microsoft.Agents.AI.Workflows.Workflow}, Action{WorkflowRegistrationOptions})"/>.
/// </summary>
/// <remarks>
/// A stored workflow carries the same settings in its <see cref="WorkflowDefinition"/>; a
/// workflow defined in code has no definition, so it chooses them here, once, when it is
/// registered.
/// </remarks>
public sealed class WorkflowRegistrationOptions
{
    /// <summary>Gets or sets a short description shown in the catalog.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets how the workflow treats more than one answer to the same pending human
    /// request. <see cref="WorkflowResponseMode.Branch"/> (the default) opens a new run for
    /// every answer; <see cref="WorkflowResponseMode.SingleAnswer"/> allows one answer per
    /// request.
    /// </summary>
    /// <remarks>
    /// A graph built in code asks for human input through a <c>RequestPort</c>; this setting
    /// applies to every request the graph publishes.
    /// </remarks>
    public WorkflowResponseMode ResponseMode { get; set; }

    /// <summary>
    /// Gets or sets how long a human request of the workflow may wait for an answer before the
    /// run that waits on it is closed. <see langword="null"/> (the default) means it waits
    /// without limit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same setting as <see cref="WorkflowDefinition.RequestTimeout"/>; its remarks apply.
    /// A run that waits for a human keeps its request event, its checkpoints and its input row
    /// until it is answered or closed. The limit is what lets a request nobody answers release
    /// them. Must be from one minute to 365 days.
    /// </para>
    /// <para>
    /// A workflow defined in code has no versions, so a run that waits takes the limit from the
    /// registration of the process that started it: a deployment that changes this value does
    /// not move the time of a request that was already published.
    /// </para>
    /// </remarks>
    public TimeSpan? RequestTimeout { get; set; }

    /// <summary>
    /// Gets or sets how long one run of the workflow may take. <see langword="null"/> (the default) uses
    /// <c>Tracon:Workflows:RunTimeout</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same setting as <see cref="WorkflowDefinition.RunTimeout"/>, with the same rule: it shortens the
    /// installation's run timeout, and it may lengthen it only up to <c>Tracon:Workflows:MaxRunTimeout</c>.
    /// The time counts the wait for a run slot. A workflow that reaches it ends <c>Canceled</c> and the
    /// error names this setting.
    /// </para>
    /// <para>
    /// <strong>A value above the ceiling stops the host at startup</strong> with an error that names the
    /// workflow: the value comes with the deployment, and a deployment that cannot honour it should not start
    /// and cut every run silently. If the ceiling is lowered later (a configuration reload), a run is cut to
    /// the new ceiling and the cut is logged. Must be from 1 millisecond to about 49.7 days.
    /// </para>
    /// </remarks>
    public TimeSpan? RunTimeout { get; set; }

    /// <summary>
    /// Gets or sets the run parameters the workflow accepts. Empty (the default) takes none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same schema as <see cref="WorkflowDefinition.Parameters"/>, with the same rules: the run's
    /// <c>parameters</c> are checked against it before the first node runs (the HTTP endpoint answers
    /// <c>400</c> early), an unknown name, a missing required value and an over-long value are refused, a
    /// queued run carries the values, and a run that is resumed or answered continues with the values it
    /// started with. A function node reads them with <c>context.GetRunParameters()</c>; an agent step
    /// receives only the names its own agent declares.
    /// </para>
    /// <para>
    /// A parameter is for identity and instruction, not for payload: pass an id and let the nodes load what
    /// they need. Without a schema every value is refused as unknown, as before. The registration is checked
    /// when it is made: a name that is not an identifier, or a repeated name, throws
    /// <see cref="ArgumentException"/>. The required parameters of a step's agent are checked when the step
    /// runs, not before the run starts as they are for a stored workflow, because the graph is built in code
    /// and its steps are not known until it is built.
    /// </para>
    /// </remarks>
    public IReadOnlyList<AgentParameter> Parameters { get; set; } = [];
}
