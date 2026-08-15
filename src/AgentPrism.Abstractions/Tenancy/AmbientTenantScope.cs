namespace AgentPrism;

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
    public static IDisposable Begin(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var previous = Ambient.Value;
        Ambient.Value = tenantId;

        return new RestoreScope(previous);
    }

    private sealed class RestoreScope(string? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}
