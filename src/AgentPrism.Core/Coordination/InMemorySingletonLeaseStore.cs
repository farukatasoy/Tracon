using System.Collections.Concurrent;

namespace AgentPrism;

/// <summary>Tek yurutucu secimi kiralarini surec bellegi icinde tutan depo.</summary>
/// <remarks>
/// <para>
/// Tek surecli bir kurulumda kira her zaman alinir (K-018): rakip yoktur.
/// Cok surecli bir kurulumda gercek koordinasyon icin bir SQL saglayicisi
/// (<c>SqlSingletonLeaseStore</c>) gerekir.
/// </para>
/// <para><strong>Sinirlari:</strong> surec omru ve tek dugum.</para>
/// </remarks>
public sealed class InMemorySingletonLeaseStore : ISingletonLeaseStore
{
    private readonly ConcurrentDictionary<string, Lease> _leases = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;

    /// <summary>Yeni bir bellek ici kira deposu olusturur.</summary>
    /// <param name="timeProvider">Zaman kaynagi. Verilmezse <see cref="TimeProvider.System"/> kullanilir.</param>
    public InMemorySingletonLeaseStore(TimeProvider? timeProvider = null)
    {
        _clock = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public ValueTask<bool> TryAcquireAsync(
        string name,
        string ownerId,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        var now = _clock.GetUtcNow();
        var expiresAt = now + duration;

        lock (_leases)
        {
            var acquired = _leases.AddOrUpdate(
                name,
                _ => new Lease(ownerId, expiresAt),
                (_, existing) => string.Equals(existing.OwnerId, ownerId, StringComparison.Ordinal) || existing.ExpiresAt < now
                    ? new Lease(ownerId, expiresAt)
                    : existing);

            return new ValueTask<bool>(
                string.Equals(acquired.OwnerId, ownerId, StringComparison.Ordinal) && acquired.ExpiresAt == expiresAt);
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> RenewAsync(
        string name,
        string ownerId,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        var expiresAt = _clock.GetUtcNow() + duration;

        lock (_leases)
        {
            if (_leases.TryGetValue(name, out var existing) && string.Equals(existing.OwnerId, ownerId, StringComparison.Ordinal))
            {
                _leases[name] = new Lease(ownerId, expiresAt);

                return new ValueTask<bool>(true);
            }

            return new ValueTask<bool>(false);
        }
    }

    /// <inheritdoc />
    public ValueTask ReleaseAsync(string name, string ownerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        lock (_leases)
        {
            if (_leases.TryGetValue(name, out var existing) && string.Equals(existing.OwnerId, ownerId, StringComparison.Ordinal))
            {
                _leases.TryRemove(name, out _);
            }
        }

        return default;
    }

    private readonly record struct Lease(string OwnerId, DateTimeOffset ExpiresAt);
}
