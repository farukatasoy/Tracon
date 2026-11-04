namespace Tracon;

/// <summary>
/// Limits concurrent voice connections per tenant.
/// </summary>
/// <remarks>
/// <para>
/// The counter is <strong>in memory</strong> and per process. In a multi-instance
/// deployment, each instance applies its own limit, so the aggregate limit scales
/// by the instance count. This is deliberate: voice connections already bind to
/// one instance through sticky sessions, and a distributed counter would require
/// coordination that costs more than the protection provides.
/// </para>
/// <para>
/// The same rationale applies to rate limiting.
/// </para>
/// </remarks>
internal sealed class VoiceConnectionLimiter
{
    private readonly TenantSlotCounter _slots = new();

    /// <summary>Initializes a new limiter.</summary>
    /// <param name="limit">The maximum concurrent connections per tenant.</param>
    public VoiceConnectionLimiter(int limit) => Limit = Math.Max(1, limit);

    /// <summary>Gets the maximum connections allowed per tenant.</summary>
    public int Limit { get; }

    /// <summary>Attempts to reserve a connection slot.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <returns>
    /// The reserved slot, or <see langword="null"/> when the limit is reached.
    /// Disposing the returned object releases the slot.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="tenantId"/> is empty.</exception>
    public VoiceConnectionLease? TryAcquire(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        // 🚨 Compare-and-swap is required (TenantSlotCounter). If two requests read together,
        // both see a value under the limit and the limit is exceeded by one.
        return _slots.TryReserve(tenantId, Limit) ? new VoiceConnectionLease(this, tenantId) : null;
    }

    /// <summary>Gets a tenant's active connection count.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <returns>The active connection count.</returns>
    public int CountFor(string tenantId)
        => tenantId is not null ? _slots.CountFor(tenantId) : 0;

    /// <summary>Gets how many tenants hold a connection: a tenant that holds none is not kept.</summary>
    internal int TrackedTenants => _slots.TrackedTenants;

    internal void Release(string tenantId) => _slots.Release(tenantId);
}

/// <summary>Represents a reserved voice connection slot.</summary>
/// <remarks>
/// Disposing it releases the slot. A slot that is not disposed remains consumed,
/// so connection code uses <c>using</c>.
/// </remarks>
internal sealed class VoiceConnectionLease : IDisposable
{
    private readonly VoiceConnectionLimiter _limiter;
    private readonly string _tenantId;
    private bool _released;

    internal VoiceConnectionLease(VoiceConnectionLimiter limiter, string tenantId)
    {
        _limiter = limiter;
        _tenantId = tenantId;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_released)
        {
            return;
        }

        _released = true;
        _limiter.Release(_tenantId);
    }
}
