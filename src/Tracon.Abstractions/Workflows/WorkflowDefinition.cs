namespace Tracon;

/// <summary>
/// Represents the full definition of a workflow, whether defined through the
/// UI or in code.
/// </summary>
/// <remarks>
/// <para>
/// A definition is a <strong>graph</strong>, not code: it only wires
/// together agents from the catalog. Users do not write new behavior, they
/// arrange existing behavior. A free-form graph (custom <c>Executor</c>
/// types) can only be defined in code, via <c>AddWorkflow(name, factory)</c>.
/// </para>
/// <para>
/// <see cref="AgentNames"/> is a list of <em>names</em> only. Each name must
/// resolve to an agent in the catalog; if it does not, compilation fails.
/// The same rule applies to <see cref="AgentDefinition.ToolNames"/> and draws
/// the same security boundary.
/// </para>
/// </remarks>
public sealed record WorkflowDefinition
{
    /// <summary>Gets the workflow's unique name. Serves as the key in the catalog and in API routes.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the display name shown in the UI. <c>Name</c> is used if left empty.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Gets the short description of what the workflow does.</summary>
    public string? Description { get; init; }

    /// <summary>Gets the built-in pattern to use.</summary>
    public required WorkflowKind Kind { get; init; }

    /// <summary>
    /// Gets the agent names to enter the graph. Order is meaningful for
    /// <see cref="WorkflowKind.Sequential"/>; for other kinds it defines the
    /// participant set.
    /// </summary>
    public IReadOnlyList<string> AgentNames { get; init; } = [];

    /// <summary>
    /// Gets the manager agent's name. Required for
    /// <see cref="WorkflowKind.Magentic"/>, unused in other patterns.
    /// </summary>
    public string? ManagerAgentName { get; init; }

    /// <summary>
    /// Gets the ordered node list for a <see cref="WorkflowKind.Sequential"/>
    /// workflow that mixes agent and function nodes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Empty for every definition that does not use a function node - which
    /// keeps <see cref="AgentNames"/> driving the graph exactly as before this
    /// field existed. When non-empty, <see cref="Kind"/> must be
    /// <see cref="WorkflowKind.Sequential"/> and <see cref="AgentNames"/> must
    /// be empty; the validator in <c>Tracon.Core</c> enforces both
    /// rules. Microsoft Agent Framework's ready-made builders for the other
    /// four patterns (<c>Concurrent</c>, <c>Handoff</c>, <c>GroupChat</c>,
    /// <c>Magentic</c>) accept only agents, so a function node cannot enter
    /// those graphs without hand-writing their orchestration logic - out of
    /// scope.
    /// </para>
    /// <para>
    /// Whether each function name is actually registered is checked at
    /// <em>save</em> time (the HTTP layer, via <see cref="IWorkflowFunctionCatalog"/>)
    /// and again at <em>compile</em> time, unlike agent names - which are
    /// checked only at compile time because the agent catalog can change
    /// between the two. The function registry cannot: it is fixed for the
    /// lifetime of the process, so checking early gives an honest guarantee.
    /// </para>
    /// </remarks>
    public IReadOnlyList<WorkflowNodeReference> Nodes { get; init; } = [];

    /// <summary>
    /// Gets the maximum number of turns. The only guard against an infinite
    /// loop in the <see cref="WorkflowKind.GroupChat"/>,
    /// <see cref="WorkflowKind.Handoff"/>, and <see cref="WorkflowKind.Magentic"/>
    /// patterns.
    /// </summary>
    public int? MaxIterations { get; init; }

    /// <summary>
    /// Gets the extra instruction that tells the model how to decide on a
    /// handoff. Used only for <see cref="WorkflowKind.Handoff"/>.
    /// </summary>
    public string? HandoffInstructions { get; init; }

    /// <summary>
    /// Gets whether the plan the manager agent builds must be approved by a
    /// human before execution starts. Applies only to
    /// <see cref="WorkflowKind.Magentic"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When enabled, Microsoft Agent Framework publishes an external request
    /// at the end of the first super-step; the run becomes
    /// <see cref="RunStatus.AwaitingInput"/> and its state is written to a
    /// checkpoint. The response is given via
    /// <c>POST /api/workflows/runs/{runId}/respond</c>: the plan is either
    /// approved or sent back with revision text.
    /// </para>
    /// <para>
    /// <strong>Cost.</strong> The manager agent runs again on every turn;
    /// a revision request makes it rebuild the plan from scratch. The default
    /// of <see langword="false"/> is deliberate: a run never stalls half-way
    /// unless a definition opts in explicitly (the no-surprises rule - zero surprises).
    /// </para>
    /// </remarks>
    public bool RequirePlanApproval { get; init; }

    /// <summary>Gets the tenant the definition belongs to. <see langword="null"/> for workflows defined in code.</summary>
    public string? TenantId { get; init; }

    /// <summary>Gets the definition version. Increments by one on every save.</summary>
    public int Version { get; init; } = 1;

    /// <summary>Gets the last modification time (UTC).</summary>
    public DateTimeOffset? UpdatedAt { get; init; }
}
