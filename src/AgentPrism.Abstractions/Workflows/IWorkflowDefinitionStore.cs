namespace AgentPrism;

/// <summary>
/// The store for workflow definitions stored in the database.
/// </summary>
/// <remarks>
/// Unlike agent definitions, no version <strong>history</strong> is kept.
/// Rationale: a workflow definition carries only a name list and a pattern;
/// the information needed to roll back already exists in the audit trail
/// (<see cref="IAuditLog"/>). An agent definition, on the other hand, carries
/// instruction text, and the old form of that text cannot be reconstructed anywhere else.
/// </remarks>
public interface IWorkflowDefinitionStore
{
    /// <summary>Fetches the definition with the given name.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="name">The workflow name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The definition; <see langword="null"/> if it does not exist.</returns>
    ValueTask<WorkflowDefinition?> GetAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>Lists all of a tenant's definitions, by name.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The definitions.</returns>
    ValueTask<IReadOnlyList<WorkflowDefinition>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the definition. The incoming <see cref="WorkflowDefinition.Version"/>
    /// value is ignored; the store determines the version number.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="definition">The definition to save.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The definition with its version number assigned.</returns>
    ValueTask<WorkflowDefinition> SaveAsync(
        string tenantId,
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes the definition.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="name">The workflow name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the delete happened.</returns>
    ValueTask<bool> DeleteAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default);
}
