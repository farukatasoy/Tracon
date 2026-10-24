namespace Tracon;

/// <summary>
/// Limits how many workflow runs one tenant has going at once inside this process.
/// </summary>
/// <remarks>
/// <para>
/// A run takes its tenant's slot first and a shared slot second, so a run held back by its
/// tenant's limit waits without holding a shared slot and never delays another tenant.
/// </para>
/// <para>
/// The per-tenant gates are counted by the runs that hold or wait on them and are dropped when
/// the last one leaves, so the table holds the tenants that have a run in flight and no more.
/// </para>
/// </remarks>
internal sealed class TenantSlotLimiter(int limit)
{
    private readonly Dictionary<string, Gate> _gates = new(StringComparer.Ordinal);

    /// <summary>Gets whether a run of <paramref name="tenantId"/> would have to wait for a tenant slot now.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <returns><see langword="true"/> when the tenant holds every slot it is allowed.</returns>
    /// <remarks>A reading, not a reservation: it can be out of date by the time the caller acts on it.</remarks>
    public bool IsFull(string tenantId)
    {
        lock (_gates)
        {
            return _gates.TryGetValue(tenantId, out var gate) && gate.Semaphore.CurrentCount == 0;
        }
    }

    /// <summary>Gets the number of tenants that have a run holding or waiting for a slot.</summary>
    public int TrackedTenants
    {
        get
        {
            lock (_gates)
            {
                return _gates.Count;
            }
        }
    }

    /// <summary>Waits for one of the tenant's slots.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="cancellationToken">Stops the wait.</param>
    /// <returns>The slot; disposing it gives the slot back, once.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled while waiting.</exception>
    public async ValueTask<IDisposable> AcquireAsync(string tenantId, CancellationToken cancellationToken)
    {
        Gate gate;

        lock (_gates)
        {
            if (!_gates.TryGetValue(tenantId, out gate!))
            {
                gate = new Gate(limit);
                _gates[tenantId] = gate;
            }

            gate.Users++;
        }

        try
        {
            await gate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Leave(tenantId, gate);
            throw;
        }

        return new TenantSlot(this, tenantId, gate);
    }

    private void Release(string tenantId, Gate gate)
    {
        gate.Semaphore.Release();
        Leave(tenantId, gate);
    }

    private void Leave(string tenantId, Gate gate)
    {
        lock (_gates)
        {
            if (--gate.Users > 0)
            {
                return;
            }

            _gates.Remove(tenantId);
        }

        gate.Semaphore.Dispose();
    }

    private sealed class Gate(int slots)
    {
        public SemaphoreSlim Semaphore { get; } = new(slots, slots);

        /// <summary>Gets or sets the runs that hold a slot or wait for one. Read and written under the table's lock.</summary>
        public int Users { get; set; }
    }

    /// <summary>One slot of a tenant; disposing it gives the slot back, once.</summary>
    private sealed class TenantSlot(TenantSlotLimiter owner, string tenantId, Gate gate) : IDisposable
    {
        private int _released;

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                owner.Release(tenantId, gate);
            }
        }
    }
}
