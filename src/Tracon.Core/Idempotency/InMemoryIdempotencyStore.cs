using System.Collections.Concurrent;

namespace Tracon;

/// <summary>A store that keeps idempotency records in process memory (first class).</summary>
/// <remarks>
/// Its behavior contract matches <c>SqlIdempotencyStore</c> exactly and shared
/// contract tests protect it. It is sufficient for a single-instance deployment.
/// A multi-instance deployment needs a SQL provider because each instance keeps
/// its own set of entries.
/// </remarks>
internal sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<(string TenantId, string Key), Entry> _entries = new();

    /// <inheritdoc />
    public ValueTask<IdempotencyReservation> ReserveAsync(
        IdempotencyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var dictionaryKey = (AmbientTenantScope.Normalize(request.TenantId), request.Key);

        while (true)
        {
            // TryAdd is atomic. If two concurrent requests use the same key, only one
            // creates the new entry and the other follows the existing-entry path below.
            if (_entries.TryAdd(dictionaryKey, new Entry(request.Fingerprint, request.CreatedAt)))
            {
                return ValueTask.FromResult(new IdempotencyReservation { State = IdempotencyState.Reserved });
            }

            if (!_entries.TryGetValue(dictionaryKey, out var existing))
            {
                continue;
            }

            // One step with the takeover, the completion and the release: two requests that race for an expired
            // reservation take it one after the other, and the second finds a reservation that is no longer old.
            lock (existing)
            {
                // Released between the lookup and the lock: the key is free, so reserve it afresh.
                if (existing.Removed)
                {
                    continue;
                }

                if (existing.State == IdempotencyState.Reserved
                    && request.ReservationTimeout is { } timeout
                    && timeout > TimeSpan.Zero
                    && request.CreatedAt - existing.CreatedAt >= timeout)
                {
                    existing.Fingerprint = request.Fingerprint;
                    existing.CreatedAt = request.CreatedAt;

                    return ValueTask.FromResult(new IdempotencyReservation { State = IdempotencyState.Reserved });
                }

                return ValueTask.FromResult(existing.State switch
                {
                    IdempotencyState.Completed when string.Equals(existing.Fingerprint, request.Fingerprint, StringComparison.Ordinal) =>
                        new IdempotencyReservation { State = IdempotencyState.Completed, Response = existing.Response },
                    IdempotencyState.Completed =>
                        new IdempotencyReservation { State = IdempotencyState.FingerprintMismatch },
                    _ => new IdempotencyReservation { State = IdempotencyState.InProgress },
                });
            }
        }
    }

    /// <inheritdoc />
    public ValueTask CompleteAsync(
        string tenantId,
        string key,
        IdempotencyResponse response,
        DateTimeOffset? reservedAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(response);
        cancellationToken.ThrowIfCancellationRequested();

        tenantId = AmbientTenantScope.Normalize(tenantId);

        if (_entries.TryGetValue((tenantId, key), out var entry))
        {
            lock (entry)
            {
                if (!entry.Removed && Owns(entry, reservedAt))
                {
                    entry.State = IdempotencyState.Completed;
                    entry.Response = response;
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask ReleaseAsync(
        string tenantId,
        string key,
        DateTimeOffset? reservedAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();

        tenantId = AmbientTenantScope.Normalize(tenantId);

        var dictionaryKey = (tenantId, key);

        if (_entries.TryGetValue(dictionaryKey, out var entry))
        {
            lock (entry)
            {
                if (!entry.Removed && Owns(entry, reservedAt))
                {
                    entry.Removed = true;
                    _entries.TryRemove(new KeyValuePair<(string TenantId, string Key), Entry>(dictionaryKey, entry));
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Says whether a caller that holds the reservation made at <paramref name="reservedAt"/> may still change the entry:
    /// the entry is still that reservation (not taken over by a later request, not completed). A caller that names no
    /// reservation changes the entry whoever holds it.
    /// </summary>
    private static bool Owns(Entry entry, DateTimeOffset? reservedAt)
        => reservedAt is not { } at || (entry.State == IdempotencyState.Reserved && entry.CreatedAt == at);

    private sealed class Entry(string fingerprint, DateTimeOffset createdAt)
    {
        public string Fingerprint { get; set; } = fingerprint;

        public DateTimeOffset CreatedAt { get; set; } = createdAt;

        public IdempotencyState State { get; set; } = IdempotencyState.Reserved;

        public IdempotencyResponse? Response { get; set; }

        /// <summary>Set under the entry's lock when it leaves the dictionary, so a request that already holds it starts over.</summary>
        public bool Removed { get; set; }
    }
}
