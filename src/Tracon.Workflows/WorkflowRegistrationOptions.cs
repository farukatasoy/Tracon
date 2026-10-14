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
}
