namespace Tracon;

/// <summary>
/// The store for workflow definitions stored in the database. Every save produces a
/// new version, old versions are not deleted, and a rollback writes an old version's
/// content back as a new one.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A version number is an identity, not a counter.</strong> Deleting a
/// definition does not delete its history, and saving the same name again continues the
/// numbering from the highest version ever written. A number therefore always names
/// the same content: a run that recorded version 3 of <c>narrated</c> can be resumed
/// with exactly that content, even after the workflow was deleted and created again.
/// (An agent definition keeps the numbering the same way, but not the history: deleting it
/// deletes its versions, and the next save continues from the highest number the name had.)
/// </para>
/// <para>
/// <strong>Tenant behavior — EXPLICIT tenant.</strong> Every member takes the tenant
/// as a parameter and never reads <c>ITenantContext</c>: the store is also called from
/// the workflow execution pipeline, where there is no HTTP context.
/// </para>
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
    /// Saves the definition. The incoming <c>WorkflowDefinition.Version</c>
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

    /// <summary>Fetches one version of a definition.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="name">The workflow name.</param>
    /// <param name="version">The version number.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The definition as it was saved at that version, with its <c>Version</c> and
    /// <c>UpdatedAt</c> set; <see langword="null"/> if that version does not exist.
    /// </returns>
    /// <remarks>
    /// The history outlives the definition: a version of a workflow that was deleted is
    /// still returned. A caller that needs the workflow to exist asks
    /// <see cref="GetAsync"/> first.
    /// </remarks>
    ValueTask<WorkflowDefinition?> GetVersionAsync(
        string tenantId,
        string name,
        int version,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every version of a definition, from newest to oldest.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="name">The workflow name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// Full snapshots, not deltas; empty when the name was never saved.
    /// </returns>
    ValueTask<IReadOnlyList<WorkflowDefinition>> ListVersionsAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes the given version current. A rollback does not delete or move anything: it
    /// saves the version's content as a new version.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="name">The workflow name.</param>
    /// <param name="version">The version number to roll back to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The definition saved as the new version.</returns>
    /// <exception cref="TraconException">That version does not exist.</exception>
    /// <remarks>
    /// The store does not judge whether the content is still valid: a function the
    /// version names may have been unregistered since. The caller validates it the way a
    /// save is validated before it calls this. When the workflow was deleted, the
    /// rollback creates it again and the numbering continues.
    /// </remarks>
    ValueTask<WorkflowDefinition> RollbackAsync(
        string tenantId,
        string name,
        int version,
        CancellationToken cancellationToken = default);
}
