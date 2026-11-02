namespace Tracon.Samples.FileRunStore;

/// <summary>
/// Returns the tenant of the context it wraps in canonical form.
/// </summary>
/// <remarks>
/// A store that reads its tenant from <see cref="ITenantContext"/> wraps the context once, so every read is
/// folded by <see cref="AmbientTenantScope.Normalize"/> and a method added later cannot skip it.
/// </remarks>
/// <param name="inner">The context whose tenant the store reads.</param>
internal sealed class CanonicalTenantContext(ITenantContext inner) : ITenantContext
{
    /// <inheritdoc />
    public string TenantId => AmbientTenantScope.Normalize(inner.TenantId);
}
