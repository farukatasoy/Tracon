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

        var dictionaryKey = (request.TenantId, request.Key);
        var entry = new Entry(request.Fingerprint);

        // TryAdd is atomic. If two concurrent requests use the same key, only one
        // creates the new entry and the other follows the existing-entry path below.
        if (_entries.TryAdd(dictionaryKey, entry))
        {
            return ValueTask.FromResult(new IdempotencyReservation { State = IdempotencyState.Reserved });
        }

        var existing = _entries[dictionaryKey];

        return ValueTask.FromResult(existing.State switch
        {
            IdempotencyState.Completed when string.Equals(existing.Fingerprint, request.Fingerprint, StringComparison.Ordinal) =>
                new IdempotencyReservation { State = IdempotencyState.Completed, Response = existing.Response },
            IdempotencyState.Completed =>
                new IdempotencyReservation { State = IdempotencyState.FingerprintMismatch },
            _ => new IdempotencyReservation { State = IdempotencyState.InProgress },
        });
    }

    /// <inheritdoc />
    public ValueTask CompleteAsync(
        string tenantId,
        string key,
        IdempotencyResponse response,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(response);

        if (_entries.TryGetValue((tenantId, key), out var entry))
        {
            entry.State = IdempotencyState.Completed;
            entry.Response = response;
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask ReleaseAsync(string tenantId, string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        _entries.TryRemove((tenantId, key), out _);

        return ValueTask.CompletedTask;
    }

    private sealed class Entry(string fingerprint)
    {
        public string Fingerprint { get; } = fingerprint;

        public IdempotencyState State { get; set; } = IdempotencyState.Reserved;

        public IdempotencyResponse? Response { get; set; }
    }
}
