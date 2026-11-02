namespace Tracon;

/// <summary>
/// Returns the tenant of the context it wraps in canonical form, whatever the wrapped context returns.
/// </summary>
/// <remarks>
/// <para>
/// The in-memory stores read the tenant through <see cref="ITenantContext"/> many times each. Wrapping the
/// context once, in the constructor, makes every read canonical: a method added to a store later cannot
/// forget the fold, which a call to <see cref="AmbientTenantScope.Normalize"/> at each read could.
/// </para>
/// <para>
/// A context that already returns the canonical form pays nothing for it: lower-casing a string that is
/// already lower case returns the same instance.
/// </para>
/// </remarks>
internal sealed class CanonicalTenantContext : ITenantContext
{
    private readonly ITenantContext _inner;

    private CanonicalTenantContext(ITenantContext inner) => _inner = inner;

    /// <inheritdoc />
    public string TenantId => AmbientTenantScope.Normalize(_inner.TenantId);

    /// <summary>Wraps <paramref name="inner"/>, unless it is already wrapped.</summary>
    /// <param name="inner">The context whose tenant the stores read.</param>
    /// <returns>A context whose <see cref="TenantId"/> is canonical.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is <see langword="null"/>.</exception>
    public static ITenantContext Wrap(ITenantContext inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        return inner is CanonicalTenantContext ? inner : new CanonicalTenantContext(inner);
    }
}
