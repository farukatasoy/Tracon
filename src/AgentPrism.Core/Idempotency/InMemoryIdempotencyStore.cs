using System.Collections.Concurrent;

namespace AgentPrism;

/// <summary>Idempotency kayitlarini islem bellegimde tutan depo (K-018: birinci sinif).</summary>
/// <remarks>
/// Davranis sozlesmesi <c>SqlIdempotencyStore</c> ile birebir aynidir ve ortak
/// sozlesme testleriyle korunur. Tek ornekli bir dagitimda yeterlidir; cok
/// ornekli bir dagitimda bir SQL saglayicisi gerekir (her ornek kendi kumesini
/// tutar).
/// </remarks>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
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

        // 🚨 TryAdd atomiktir: iki eszamanli istek ayni anahtarla gelirse yalniz
        // biri yeni kaydi acar, digeri asagidaki mevcut kayit dalina duser.
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
