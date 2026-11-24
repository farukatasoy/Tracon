namespace Tracon;

/// <summary>
/// The built-in job handler keys that run only in the installation's default tenant, because a job of one of them
/// reaches the data of every tenant.
/// </summary>
/// <remarks>
/// The handler of such a key refuses a job of any other tenant when it runs. The HTTP schedule endpoint reads this list
/// too, so a tenant is told so when it saves the schedule and is not shown the key as one it may use.
/// </remarks>
internal static class HostOnlyJobHandlerKeys
{
    /// <summary>Determines whether <paramref name="handlerKey"/> runs only in the default tenant.</summary>
    /// <param name="handlerKey">The handler key.</param>
    /// <returns><see langword="true"/> for a key that is host-only.</returns>
    public static bool Contains(string? handlerKey)
        => string.Equals(handlerKey, JobHandlerKeys.RetentionFanOut, StringComparison.Ordinal);

    /// <summary>Determines whether <paramref name="tenantId"/> is the installation's default tenant.</summary>
    /// <param name="tenantId">The tenant identifier, in the canonical form.</param>
    /// <param name="defaultTenantId">The configured default tenant (<see cref="TraconOptions.DefaultTenantId"/>).</param>
    /// <returns><see langword="true"/> when the two name the same tenant.</returns>
    public static bool IsDefaultTenant(string tenantId, string defaultTenantId)
        => AmbientTenantScope.TryNormalize(tenantId, out var tenant)
        && AmbientTenantScope.TryNormalize(defaultTenantId, out var host)
        && string.Equals(tenant, host, StringComparison.Ordinal);
}
