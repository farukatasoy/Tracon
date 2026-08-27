namespace AgentPrism;

/// <summary>
/// The store for agent definitions kept in the database. Versioning and rollback
/// support are required: every save produces a new version, and old versions are
/// not deleted.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Tenant behavior — AMBIENT tenant.</strong> No member takes a tenant
/// parameter; every method filters by the current <c>ITenantContext</c>
/// internally.
/// </para>
/// <para>
/// <strong>Why this differs from <c>IAgentSkillStore</c>/<c>ISkillScriptGrantStore</c>.</strong>
/// Those two sibling stores (added later, for the skills feature) take an
/// EXPECTED tenant as an explicit parameter on every method instead. Both
/// shapes filter correctly — there is no known cross-tenant leak from either
/// one — and there is no deeper design rule that decides which a given store
/// should use: this is a generational split, not a principled one. This store
/// followed the earlier convention shared with <c>IRunStore</c> and
/// <c>ISessionStore</c> (read <c>ITenantContext</c> internally); the skills
/// stores followed the convention shared with most later admin-CRUD stores
/// (an explicit parameter, no ambient fallback at all). A third-party
/// implementer must read each interface's OWN tenant-mode remarks rather than
/// assume consistency across a cluster.
/// </para>
/// </remarks>
public interface IAgentDefinitionStore
{
    /// <summary>Returns the current version of the definition with the given name.</summary>
    /// <param name="name">The agent name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The definition, or <see langword="null"/> when it does not exist.</returns>
    ValueTask<AgentDefinition?> GetAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Returns the given version of the definition with the given name.</summary>
    /// <param name="name">The agent name.</param>
    /// <param name="version">The requested version number.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The definition, or <see langword="null"/> when that version does not exist.</returns>
    ValueTask<AgentDefinition?> GetVersionAsync(string name, int version, CancellationToken cancellationToken = default);

    /// <summary>Lists the current version of every definition.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The definitions.</returns>
    ValueTask<IReadOnlyList<AgentDefinition>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the definition and produces a new version. The
    /// <c>AgentDefinition.Version</c> value of the incoming definition is ignored;
    /// the store decides the version number.
    /// </summary>
    /// <param name="definition">The definition to save.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The definition with the new version number assigned.</returns>
    ValueTask<AgentDefinition> SaveAsync(AgentDefinition definition, CancellationToken cancellationToken = default);

    /// <summary>Deletes the definition and every one of its versions.</summary>
    /// <param name="name">The agent name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the delete happened.</returns>
    ValueTask<bool> DeleteAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Lists every version of a definition, from newest to oldest.</summary>
    /// <param name="name">The agent name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The version history.</returns>
    ValueTask<IReadOnlyList<AgentDefinition>> ListVersionsAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes the given version current. A rollback does not delete the old version; it
    /// saves its content as a new version.
    /// </summary>
    /// <param name="name">The agent name.</param>
    /// <param name="version">The version number to roll back to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The definition saved as the new version.</returns>
    ValueTask<AgentDefinition> RollbackAsync(string name, int version, CancellationToken cancellationToken = default);
}
