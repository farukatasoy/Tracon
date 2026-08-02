using System.Collections.Concurrent;

namespace AgentPrism;

/// <summary>
/// Ayni anda calisan script sayisini hem toplamda hem kiraci basina sinirlar.
/// </summary>
/// <remarks>
/// Tek bir kiraci, tum barindirma ortamini tuketen bir script seli baslatabilir.
/// Kiraci basina sinir bunu engeller; toplam sinir sunucuyu korur.
/// </remarks>
internal sealed class SkillScriptConcurrencyLimiter : IDisposable
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _perTenant = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _total;
    private readonly int _perTenantLimit;

    public SkillScriptConcurrencyLimiter(AgentPrismSkillScriptOptions options)
    {
        _total = new SemaphoreSlim(options.MaxConcurrentTotal, options.MaxConcurrentTotal);
        _perTenantLimit = options.MaxConcurrentPerTenant;
    }

    /// <summary>Yer acilana kadar bekler ve birakilinca kotayi geri veren bir nesne dondurur.</summary>
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
