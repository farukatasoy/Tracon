namespace Tracon;

/// <summary>The store for a tenant's run-time skill definitions.</summary>
/// <remarks>
/// <strong>Tenant behavior — EXPECTED tenant, uniformly, with NO ambient
/// fallback.</strong> Every member takes <c>tenantId</c> as an explicit
/// parameter; an implementation does not read <c>ITenantContext</c> at all.
/// This differs from the neighboring <see cref="IAgentDefinitionStore"/>,
/// which reads the ambient tenant internally instead — see that interface's
/// remarks for why the shape is not uniform across the cluster.
/// </remarks>
public interface IAgentSkillStore
{
    /// <summary>Lists all of a tenant's skills.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The skills, ordered by name.</returns>
    ValueTask<IReadOnlyList<AgentSkillDefinition>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches the skill matching the given name within the tenant.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="name">The skill name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The skill; <see langword="null"/> if it does not exist.</returns>
    ValueTask<AgentSkillDefinition?> GetAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>Creates or updates the skill.</summary>
    /// <param name="skill">The skill to save.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The skill with its identifier, timestamps, and version assigned.</returns>
    ValueTask<AgentSkillDefinition> SaveAsync(
        AgentSkillDefinition skill,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a tenant's skill.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="name">The skill name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the skill was deleted.</returns>
    ValueTask<bool> DeleteAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default);
}
