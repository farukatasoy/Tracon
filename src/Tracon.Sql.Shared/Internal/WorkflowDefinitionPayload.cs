namespace Tracon;

/// <summary>
/// The part of a workflow definition that is written to the <c>jsonb</c> column.
/// </summary>
/// <remarks>
/// <para>
/// The name, version, tenant and update time live in <em>columns</em> and that is
/// the single source of truth. Repeating those fields inside the <c>jsonb</c> as
/// well would create two records of the same fact.
/// </para>
/// <para>
/// <strong>This DTO is written by hand and is NOT synchronized automatically
/// with <see cref="WorkflowDefinition"/>.</strong> When a new field is added to the
/// definition it must be added here as well; otherwise the field is
/// <em>silently</em> not written to PostgreSQL. Neither the build nor a test
/// breaks - only a round-trip test catches it. The same trap was hit once before
/// with <c>AgentDefinitionPayload</c>.
/// </para>
/// </remarks>
internal sealed record WorkflowDefinitionPayload
{
    /// <summary>Gets the name shown in the user interface.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Gets the workflow description.</summary>
    public string? Description { get; init; }

    /// <summary>Gets the built-in pattern in use.</summary>
    public WorkflowKind Kind { get; init; }

    /// <summary>Gets the names of the agents that enter the graph.</summary>
    public IReadOnlyList<string> AgentNames { get; init; } = [];

    /// <summary>Gets the mixed agent/function node list (Sequential only).</summary>
    public IReadOnlyList<WorkflowNodeReference> Nodes { get; init; } = [];

    /// <summary>Gets the name of the manager agent.</summary>
    public string? ManagerAgentName { get; init; }

    /// <summary>Gets the maximum number of iterations.</summary>
    public int? MaxIterations { get; init; }

    /// <summary>Gets the handoff instruction.</summary>
    public string? HandoffInstructions { get; init; }

    /// <summary>Gets a value indicating whether the plan of the manager agent needs human approval.</summary>
    public bool RequirePlanApproval { get; init; }

    /// <summary>Converts the content of a definition into a payload.</summary>
    /// <param name="definition">The source definition.</param>
    /// <returns>The payload to serialize.</returns>
    public static WorkflowDefinitionPayload FromDefinition(WorkflowDefinition definition)
        => new()
        {
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            Kind = definition.Kind,
            AgentNames = definition.AgentNames,
            Nodes = definition.Nodes,
            ManagerAgentName = definition.ManagerAgentName,
            MaxIterations = definition.MaxIterations,
            HandoffInstructions = definition.HandoffInstructions,
            RequirePlanApproval = definition.RequirePlanApproval,
        };

    /// <summary>Builds the definition by merging the payload with the information from the columns.</summary>
    /// <param name="name">The workflow name.</param>
    /// <param name="version">The version number.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="updatedAt">The last update time.</param>
    /// <returns>The full definition.</returns>
    public WorkflowDefinition ToDefinition(string name, int version, string tenantId, DateTimeOffset updatedAt)
        => new()
        {
            Name = name,
            DisplayName = DisplayName,
            Description = Description,
            Kind = Kind,
            AgentNames = AgentNames,
            Nodes = Nodes,
            ManagerAgentName = ManagerAgentName,
            MaxIterations = MaxIterations,
            HandoffInstructions = HandoffInstructions,
            RequirePlanApproval = RequirePlanApproval,
            TenantId = tenantId,
            Version = version,
            UpdatedAt = updatedAt,
        };
}
