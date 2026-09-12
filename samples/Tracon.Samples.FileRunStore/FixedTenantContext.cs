namespace Tracon.Samples.FileRunStore;

/// <summary>A single-tenant <see cref="ITenantContext"/> that always reports the same tenant.</summary>
/// <param name="tenantId">The fixed tenant id.</param>
internal sealed class FixedTenantContext(string tenantId) : ITenantContext
{
    /// <inheritdoc />
    public string TenantId { get; } = tenantId;
}
