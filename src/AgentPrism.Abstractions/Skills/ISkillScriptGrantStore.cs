namespace AgentPrism;

/// <summary>The store for skill script execution grants.</summary>
public interface ISkillScriptGrantStore
{
    /// <summary>Lists all of a tenant's grant records.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>All grants, including revoked and expired records.</returns>
    ValueTask<IReadOnlyList<SkillScriptGrant>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the active grant for the given script.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="skillName">The skill name.</param>
    /// <param name="scriptName">The script name.</param>
    /// <param name="instant">The moment to check validity at.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The script-specific grant first, or the skill-wide grant otherwise.
    /// <see langword="null"/> if no active grant exists.
    /// </returns>
    ValueTask<SkillScriptGrant?> FindActiveAsync(
        string tenantId,
        string skillName,
        string scriptName,
        DateTimeOffset instant,
        CancellationToken cancellationToken = default);

    /// <summary>Grants access, or updates an existing grant.</summary>
    /// <param name="grant">The grant to save.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The grant with its identifier and timestamp assigned.</returns>
    ValueTask<SkillScriptGrant> GrantAsync(
        SkillScriptGrant grant,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes a grant.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="skillName">The skill name.</param>
    /// <param name="scriptName">The script name; <see langword="null"/> for the skill-wide grant.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if an active grant was revoked.</returns>
    ValueTask<bool> RevokeAsync(
        string tenantId,
        string skillName,
        string? scriptName,
        CancellationToken cancellationToken = default);
}
