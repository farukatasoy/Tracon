namespace AgentPrism;

/// <summary>The store for per-tenant model provider egress policies (F-119, phase 65).</summary>
public interface ITenantEgressPolicyStore
{
    /// <summary>Reads a tenant's policy.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The policy; <see langword="null"/> if the tenant has none saved, which
    /// means the tenant is unrestricted (K1).
    /// </returns>
    ValueTask<TenantEgressPolicy?> GetAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Creates or replaces a tenant's policy.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="allowedProviders">The closed set of allowed provider names.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The saved policy.</returns>
    ValueTask<TenantEgressPolicy> UpsertAsync(string tenantId, IReadOnlyList<string> allowedProviders, CancellationToken cancellationToken = default);

    /// <summary>Deletes a tenant's policy, making the tenant unrestricted again.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if a policy was found and deleted.</returns>
    ValueTask<bool> DeleteAsync(string tenantId, CancellationToken cancellationToken = default);
}
