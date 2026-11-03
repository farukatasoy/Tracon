namespace Tracon;

/// <summary>
/// The tenant value of a retention policy: a tenant identifier, or <c>"*"</c> for the default of every tenant.
/// </summary>
/// <remarks>
/// <c>"*"</c> is not a tenant. <see cref="AmbientTenantScope.Normalize"/> refuses it, so that no tenant context can
/// return it and read or write the default policies as if it were one. The retention policy stores are the only code
/// that names the default, and they pass the value through here instead of through the tenant rule.
/// </remarks>
internal static class RetentionPolicyTenant
{
    /// <summary>The value that stands for the default policy of every tenant.</summary>
    public const string AllTenants = "*";

    /// <summary>Returns the stored form of a policy's tenant value.</summary>
    /// <param name="tenantId">A tenant identifier, or <see cref="AllTenants"/>.</param>
    /// <returns>The canonical tenant identifier; <see cref="AllTenants"/> unchanged.</returns>
    /// <exception cref="ArgumentException">The value is blank or holds a character outside the tenant alphabet.</exception>
    public static string Normalize(string tenantId)
        => string.Equals(tenantId, AllTenants, StringComparison.Ordinal) ? AllTenants : AmbientTenantScope.Normalize(tenantId);
}
