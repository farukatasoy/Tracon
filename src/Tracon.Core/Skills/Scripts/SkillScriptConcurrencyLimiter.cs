using System.Collections.Concurrent;

namespace Tracon;

/// <summary>
/// Limits the number of concurrently running scripts both globally and per tenant.
/// </summary>
/// <remarks>
/// One tenant can start a flood of scripts that consumes the hosting environment.
/// The per-tenant limit prevents this, and the global limit protects the server.
/// </remarks>
internal sealed class SkillScriptConcurrencyLimiter : IDisposable
{
    // 🚨 A tenant is tracked only while a script of it holds or waits for a slot. An entry kept for every
    // identifier ever seen grows with the number of identifiers: a caller's tenancy settings decide what an
    // identifier may be, and with header resolution on and no allow list that is any well-formed string.
    private readonly Dictionary<string, TenantSlot> _perTenant = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _total;
    private readonly int _perTenantLimit;

    public SkillScriptConcurrencyLimiter(TraconSkillScriptOptions options)
    {
        _total = new SemaphoreSlim(options.MaxConcurrentTotal, options.MaxConcurrentTotal);
        _perTenantLimit = options.MaxConcurrentPerTenant;
    }

    /// <summary>Gets how many tenants the limiter currently tracks.</summary>
    internal int TrackedTenantCount
    {
        get
        {
            lock (_perTenant)
            {
                return _perTenant.Count;
            }
        }
    }

    /// <summary>Waits for capacity and returns an object that releases the quota when disposed.</summary>
    public async ValueTask<IDisposable> AcquireAsync(string tenantId, CancellationToken cancellationToken)
    {
        TenantSlot tenant;

        lock (_perTenant)
        {
            if (!_perTenant.TryGetValue(tenantId, out tenant!))
            {
                tenant = new TenantSlot(new SemaphoreSlim(_perTenantLimit, _perTenantLimit));
                _perTenant.Add(tenantId, tenant);
            }

            tenant.Users++;
        }

        try
        {
            await tenant.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                await _total.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                tenant.Semaphore.Release();
                throw;
            }
        }
        catch
        {
            Forget(tenantId, tenant);
            throw;
        }

        return new Lease(this, tenantId, tenant);
    }

    public void Dispose()
    {
        _total.Dispose();

        lock (_perTenant)
        {
            foreach (var slot in _perTenant.Values)
            {
                slot.Semaphore.Dispose();
            }

            _perTenant.Clear();
        }
    }

    /// <summary>Drops one user of the tenant's slot, and the slot itself when nobody holds or waits for it.</summary>
    private void Forget(string tenantId, TenantSlot slot)
    {
        lock (_perTenant)
        {
            if (--slot.Users > 0)
            {
                return;
            }

            // Only the entry this user used: a slot that was replaced after it emptied belongs to someone else.
            if (_perTenant.TryGetValue(tenantId, out var current) && ReferenceEquals(current, slot))
            {
                _perTenant.Remove(tenantId);
            }

            slot.Semaphore.Dispose();
        }
    }

    private sealed class TenantSlot(SemaphoreSlim semaphore)
    {
        public SemaphoreSlim Semaphore { get; } = semaphore;

        /// <summary>Gets or sets the scripts that hold or wait for the slot; changed only while holding the lock on the tenant table.</summary>
        public int Users { get; set; }
    }

    private sealed class Lease(SkillScriptConcurrencyLimiter owner, string tenantId, TenantSlot tenant) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return;
            }

            owner._total.Release();
            tenant.Semaphore.Release();
            owner.Forget(tenantId, tenant);
        }
    }
}
