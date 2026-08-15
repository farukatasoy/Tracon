namespace AgentPrism;

/// <summary>
/// An immutable tenant context that always returns the same tenant.
/// </summary>
/// <remarks>
/// <para>
/// Added in Phase 41. In-memory stores read the tenant through <see cref="ITenantContext"/>.
/// When a store is created outside DI directly with <c>new</c>, this type supplies the
/// context if one is not provided, and the store behaves as single tenant.
/// </para>
/// <para>
/// Making a store tenant context <em>optional</em> does not make filtering optional.
/// A context always exists. Its value is simply fixed when it is not supplied.
/// Filtering occurs in every code path.
/// </para>
/// </remarks>
/// <param name="tenantId">The tenant identifier to return.</param>
public sealed class FixedTenantContext(string tenantId) : ITenantContext
{
    /// <summary>
    /// The shared instance that carries the default value of
    /// <see cref="AgentPrismOptions.DefaultTenantId"/>.
    /// </summary>
    public static FixedTenantContext Default { get; } = new("default");

    /// <inheritdoc />
    public string TenantId { get; } = tenantId;
}
