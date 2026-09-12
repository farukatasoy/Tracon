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
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _perTenant = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _total;
    private readonly int _perTenantLimit;

    public SkillScriptConcurrencyLimiter(TraconSkillScriptOptions options)
    {
        _total = new SemaphoreSlim(options.MaxConcurrentTotal, options.MaxConcurrentTotal);
        _perTenantLimit = options.MaxConcurrentPerTenant;
    }

    /// <summary>Waits for capacity and returns an object that releases the quota when disposed.</summary>
    public async ValueTask<IDisposable> AcquireAsync(string tenantId, CancellationToken cancellationToken)
    {
        var tenant = _perTenant.GetOrAdd(tenantId, _ => new SemaphoreSlim(_perTenantLimit, _perTenantLimit));

        await tenant.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _total.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            tenant.Release();
            throw;
        }

        return new Lease(this, tenant);
    }

    public void Dispose()
    {
        _total.Dispose();

        foreach (var semaphore in _perTenant.Values)
        {
            semaphore.Dispose();
        }

        _perTenant.Clear();
    }

    private sealed class Lease(SkillScriptConcurrencyLimiter owner, SemaphoreSlim tenant) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return;
            }

            owner._total.Release();
            tenant.Release();
        }
    }
}
