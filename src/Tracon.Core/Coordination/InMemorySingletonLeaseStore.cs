using System.Collections.Concurrent;

namespace Tracon;

/// <summary>A store that keeps singleton execution leases in process memory.</summary>
/// <remarks>
/// <para>
/// In a single-process deployment, it always acquires the lease because
/// there is no competitor. A multi-process deployment needs a SQL provider,
/// <c>SqlSingletonLeaseStore</c>, for real coordination.
/// </para>
/// <para><strong>Limits:</strong> process lifetime and a single node.</para>
/// </remarks>
internal sealed class InMemorySingletonLeaseStore : ISingletonLeaseStore
{
    private readonly ConcurrentDictionary<string, Lease> _leases = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;

    /// <summary>Initializes a new in-memory lease store.</summary>
    /// <param name="timeProvider">The time provider. Uses <see cref="TimeProvider.System"/> when omitted.</param>
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
