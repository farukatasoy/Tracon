namespace AgentPrism;

/// <summary>Bir idempotency ayirma istegi.</summary>
public sealed record IdempotencyRequest
{
    /// <summary>Kiraci kimligi.</summary>
    public required string TenantId { get; init; }

    /// <summary>Istemcinin <c>Idempotency-Key</c> basliginda gonderdigi anahtar.</summary>
    public required string Key { get; init; }

    /// <summary>HTTP metodu + yol + ham govde ozeti (SHA-256).</summary>
    public required string Fingerprint { get; init; }

    /// <summary>Ayirmanin yapildigi zaman (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Bir <see cref="IIdempotencyStore.ReserveAsync"/> cagrisinin sonucu.</summary>
public sealed record IdempotencyReservation
{
    /// <summary>Anahtarin bulundugu durum.</summary>
    public required IdempotencyState State { get; init; }

    /// <summary>Yalnizca <see cref="IdempotencyState.Completed"/> icin dolar.</summary>
    public IdempotencyResponse? Response { get; init; }
}

/// <summary>Bir idempotency anahtarinin dort olasi durumu.</summary>
public enum IdempotencyState
{
    /// <summary>Anahtar yeni ayrildi; istek normal islenir.</summary>
    Reserved = 0,

    /// <summary>Ayni anahtar hala isleniyor. Yanit: <c>409 Conflict</c>.</summary>
    InProgress = 1,

    /// <summary>Tamamlanmis ve govde parmak izi ayni. Saklanan yanit dondurulur.</summary>
    Completed = 2,

    /// <summary>Tamamlanmis ama govde parmak izi FARKLI. Yanit: <c>422 Unprocessable Content</c>.</summary>
    FingerprintMismatch = 3,
}

/// <summary>Saklanan HTTP yaniti.</summary>
public sealed record IdempotencyResponse
{
    /// <summary>Orijinal yanitin durum kodu.</summary>
    public required int StatusCode { get; init; }

    /// <summary>Orijinal yanitin icerik tipi.</summary>
    public required string ContentType { get; init; }

    /// <summary>Orijinal yanitin ham govdesi.</summary>
    public required string Body { get; init; }

    /// <summary>Yanit bir calistirma urettiyse kimligi; aksi halde <see langword="null"/>.</summary>
    public Guid? RunId { get; init; }

    /// <summary>
    /// Orijinal yanitin, govde/durum kodu/icerik tipi disinda saklanmasi
    /// gereken HTTP baslikları (orn. <c>Location</c>, <c>Preference-Applied</c>).
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
