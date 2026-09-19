namespace Tracon;

/// <summary>
/// The ambient passing mechanism for running an operation executed in the
/// background (outside an HTTP request) as a specific tenant.
/// </summary>
/// <remarks>
/// The <see cref="ITenantContext"/> implementations (the single-tenant
/// default, the HTTP-based multi-tenant resolver) are singletons and read the
/// tenant either from a fixed default or from <c>HttpContext</c>. Which
/// tenant a scheduled job runs for is neither fixed nor lives in an HTTP
/// context — it is carried inside <c>JobRecord.TenantId</c>. This class
/// provides a pass-through using the same <see cref="AsyncLocal{T}"/> pattern
/// <c>IHttpContextAccessor</c> uses: while the value is set, both
/// <see cref="ITenantContext"/> implementations check it before their own default resolution.
/// </remarks>
public static class AmbientTenantScope
{
    private static readonly AsyncLocal<string?> Ambient = new();

    /// <summary>The current ambient tenant if set right now; <see langword="null"/> otherwise.</summary>
    public static string? Current => Ambient.Value;

    /// <summary>
    /// Sets the ambient tenant for the duration of the scope.
    /// </summary>
    /// <param name="tenantId">The tenant identifier to use for the duration of the scope.</param>
    /// <returns>
    /// An object that restores the previous value when
    /// <see cref="IDisposable.Dispose"/> is called. Nested use is safe.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="tenantId"/> is empty or whitespace only.</exception>
    /// <remarks>
    /// The value is stored in the form <see cref="Normalize"/> returns, so
    /// <see cref="Current"/> is always canonical whatever letter case the
    /// caller wrote.
    /// </remarks>
    public static IDisposable Begin(string tenantId)
    {
        var previous = Ambient.Value;
        Ambient.Value = Normalize(tenantId);

        return new RestoreScope(previous);
    }

    /// <summary>
    /// Returns the canonical form a tenant identifier is compared and stored in.
    /// </summary>
    /// <param name="tenantId">The tenant identifier as written by the caller.</param>
    /// <returns>The canonical (invariant lower-case) form.</returns>
    /// <remarks>
    /// <para>
    /// The tenant identifier is the product's primary isolation key, and it is
    /// matched <strong>case-insensitively</strong>. Every layer that compares
    /// or persists one applies this first: the tenant context, the allow-list
    /// check, the endpoint filter, and every store.
    /// </para>
    /// <para>
    /// Normalizing the <em>value</em> (rather than comparing case-insensitively
    /// at query time) is deliberate, and it is the same choice
    /// <see cref="TenantProviderBinding.NormalizeProviderName"/> makes: a bare
    /// <c>=</c> predicate then behaves identically on PostgreSQL, SQLite and
    /// SQL Server whatever their collation, the primary key keeps rejecting
    /// duplicates on all three, and the index stays usable. Without it the
    /// authorization layer and the storage layer disagree about whether
    /// <c>acme</c> and <c>Acme</c> are one tenant — on SQL Server's
    /// case-insensitive default collation they are one, on PostgreSQL and
    /// SQLite they are two.
    /// </para>
    /// <para>
    /// A consumer that implements <see cref="ITenantContext"/> or a store
    /// itself must apply this to the value it returns, writes
    /// <em>and</em> queries with. A store that skips it is case-sensitive
    /// while the rest of the system is not.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="tenantId"/> is null, empty or whitespace only.</exception>
    public static string Normalize(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        // ToLowerInvariant, never ToLower: under tr-TR the latter maps 'I' to
        // the DOTLESS lower-case i, so the same identifier would fold to two
        // different canonical values depending on the server's culture.
        return tenantId.ToLowerInvariant();
    }

    /// <summary>
    /// Returns the canonical form of an optional tenant identifier.
    /// </summary>
    /// <param name="tenantId">The tenant identifier, or <see langword="null"/>.</param>
    /// <returns>
    /// The canonical form; <see langword="null"/> when <paramref name="tenantId"/>
    /// is <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// A <see langword="null"/> tenant means "every tenant" in the query types
    /// that accept one, so it passes through unchanged.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="tenantId"/> is empty or whitespace only.</exception>
    public static string? NormalizeOrNull(string? tenantId)
        => tenantId is null ? null : Normalize(tenantId);

    private sealed class RestoreScope(string? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}
