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
    /// <para>
    /// A function node may carry <see cref="WorkflowNodeReference.Settings"/>, checked
    /// the same way against the settings type its function declares
    /// (<see cref="IWorkflowFunctionCatalog.ValidateSettings"/>) and read again at
    /// compile time, because that type can change between deployments.
    /// </para>
    /// <para>
    /// A chain of agents only may be stored either way: as <see cref="AgentNames"/>
    /// or as <see cref="Nodes"/>, and the server accepts both. The console writes
    /// <see cref="Nodes"/> only when the chain holds at least one function node.
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
    /// unless a definition opts in explicitly (the no-surprises rule).
    /// </para>
    /// </remarks>
    public bool RequirePlanApproval { get; init; }

    /// <summary>
    /// Gets the run parameters the workflow accepts. Empty for a workflow that
    /// takes none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A run supplies values for these names (<see cref="WorkflowRunRequest.Parameters"/>).
    /// The set is the workflow's contract: each agent step receives the values
    /// whose names ITS OWN schema declares, and binds them into its
    /// instructions the way a direct run of that agent would. A name the
    /// workflow declares but a step does not is dropped for that step.
    /// </para>
    /// <para>
    /// Only the structure is checked when the definition is saved (valid,
    /// unique names). Whether every participant's required parameters are
    /// covered is checked when a run starts: the participants live in the agent
    /// catalog, which can change after the workflow is saved.
    /// </para>
    /// <para>
    /// A workflow defined in code (<c>AddWorkflow</c>) carries no definition and
    /// therefore declares no parameters; a value supplied to it is rejected.
    /// <see cref="AgentParameter.Kind"/> labels the value for the console only;
    /// every value travels as a string.
    /// </para>
    /// </remarks>
    public IReadOnlyList<AgentParameter> Parameters { get; init; } = [];

    /// <summary>
    /// Gets the longest time one run of this workflow may take.
    /// <see langword="null"/> (the default) uses the installation's
    /// <c>Tracon:Workflows:RunTimeout</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The value replaces the installation's run timeout for this workflow only,
    /// and it behaves the same way: the deadline starts when the run starts (a
    /// wait for a <c>MaxConcurrentRuns</c> slot counts too), it cancels the token
    /// every node is given, and a run that reaches it ends
    /// <see cref="RunStatus.Canceled"/> with an error that names this field.
    /// </para>
    /// <para>
    /// <strong>It may only shorten the installation's value unless the host allows
    /// more.</strong> A running workflow holds one of the slots that every tenant
    /// shares, so the host decides how long one may hold it:
    /// <c>Tracon:Workflows:MaxRunTimeout</c> is the ceiling. While it is not set, a
    /// value above <c>Tracon:Workflows:RunTimeout</c> is refused when the definition
    /// is saved (<c>400</c>). A stored value above a ceiling that was lowered later
    /// still runs, cut to the ceiling.
    /// </para>
    /// <para>
    /// Must be from one millisecond to about 49.7 days. A node's own timeout
    /// (<see cref="WorkflowNodeReference.Timeout"/>) is not checked against this
    /// value: the two answer different questions, and whichever ends first stops the
    /// node.
    /// </para>
    /// <para>
    /// A <see cref="WorkflowKind.Sequential"/> chain bounds each step with
    /// <see cref="WorkflowNodeReference.Timeout"/>; the other patterns
    /// (<see cref="AgentNames"/>) have no nodes, so this value and the installation's
    /// <c>Tracon:AgentGraph:ChildDeadline</c> bound them.
    /// </para>
    /// </remarks>
    public TimeSpan? RunTimeout { get; init; }

    /// <summary>Gets the tenant the definition belongs to. <see langword="null"/> for workflows defined in code.</summary>
    public string? TenantId { get; init; }

    /// <summary>Gets the definition version. Increments by one on every save.</summary>
    public int Version { get; init; } = 1;

    /// <summary>Gets the last modification time (UTC).</summary>
    public DateTimeOffset? UpdatedAt { get; init; }
}
