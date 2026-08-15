namespace AgentPrism;

/// <summary>An idempotency reservation request.</summary>
public sealed record IdempotencyRequest
{
    /// <summary>The tenant identifier.</summary>
    public required string TenantId { get; init; }

    /// <summary>The key the client sent in the <c>Idempotency-Key</c> header.</summary>
    public required string Key { get; init; }

    /// <summary>The HTTP method + path + raw body digest (SHA-256).</summary>
    public required string Fingerprint { get; init; }

    /// <summary>The time the reservation was made (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>The result of an <see cref="IIdempotencyStore.ReserveAsync"/> call.</summary>
public sealed record IdempotencyReservation
{
    /// <summary>The state the key was found in.</summary>
    public required IdempotencyState State { get; init; }

    /// <summary>Populated only for <see cref="IdempotencyState.Completed"/>.</summary>
    public IdempotencyResponse? Response { get; init; }
}

/// <summary>The four possible states of an idempotency key.</summary>
public enum IdempotencyState
{
    /// <summary>The key was just reserved; the request is processed normally.</summary>
    Reserved = 0,

    /// <summary>The same key is still being processed. Response: <c>409 Conflict</c>.</summary>
    InProgress = 1,

    /// <summary>Completed, and the body fingerprint matches. The stored response is returned.</summary>
    Completed = 2,

    /// <summary>Completed, but the body fingerprint DIFFERS. Response: <c>422 Unprocessable Content</c>.</summary>
    FingerprintMismatch = 3,
}

/// <summary>The stored HTTP response.</summary>
public sealed record IdempotencyResponse
{
    /// <summary>The original response's status code.</summary>
    public required int StatusCode { get; init; }

    /// <summary>The original response's content type.</summary>
    public required string ContentType { get; init; }

    /// <summary>The original response's raw body.</summary>
    public required string Body { get; init; }

    /// <summary>The identifier of the run, if the response produced one; otherwise <see langword="null"/>.</summary>
    public Guid? RunId { get; init; }

    /// <summary>
    /// The HTTP headers of the original response that must be preserved
    /// beyond the body/status code/content type (for example, <c>Location</c>,
    /// <c>Preference-Applied</c>).
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
