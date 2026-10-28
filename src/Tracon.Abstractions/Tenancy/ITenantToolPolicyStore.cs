namespace Tracon;

/// <summary>The store for per-tenant tool policies.</summary>
/// <remarks>
/// Tracon reads the policy before every tool call that runs through its tool chain (see
/// <see cref="TenantToolPolicy"/> for the rule). A store that reads a stale or wrong tenant's row decides
/// which tool a tenant may run, so a custom store must apply <see cref="AmbientTenantScope.Normalize"/> to the
/// tenant it writes and reads with.
/// </remarks>
public interface ITenantToolPolicyStore
{
    /// <summary>Reads a tenant's policy.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The policy; <see langword="null"/> if the tenant has none saved, which means the tenant is unrestricted
    /// unless <c>Tracon:Tools:RequireTenantPolicy</c> is on.
    /// </returns>
    ValueTask<TenantToolPolicy?> GetAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Creates or replaces a tenant's policy.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="allowedTools">The closed set of tool names the tenant may run.</param>
    /// <param name="grantedPermissions">The closed set of permissions granted to the tenant.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The saved policy.</returns>
    ValueTask<TenantToolPolicy> UpsertAsync(
        string tenantId,
        IReadOnlyList<string> allowedTools,
        IReadOnlyList<string> grantedPermissions,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a tenant's policy.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if a policy was found and deleted.</returns>
    ValueTask<bool> DeleteAsync(string tenantId, CancellationToken cancellationToken = default);
}
