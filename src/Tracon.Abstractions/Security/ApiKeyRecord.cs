namespace Tracon;

/// <summary>
/// An API key's database-stored view, WHICH CARRIES NO RAW VALUE.
/// </summary>
/// <remarks>
/// This record <strong>has no</strong> raw key. Only an irreversible
/// SHA-256 digest sits in the database (nowhere but <see cref="KeyPrefix"/>);
/// the raw value is returned <strong>once</strong>, only at creation time,
/// via <see cref="ApiKeyCreationResult.PlaintextKey"/>.
/// </remarks>
public sealed record ApiKeyRecord
{
    /// <summary>The key identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>The tenant the key is bound to. On authentication, the tenant is resolved FROM HERE.</summary>
    public required string TenantId { get; init; }

    /// <summary>The name for the operator to recognize the key by.</summary>
    public required string Name { get; init; }

    /// <summary>The raw value's first characters; used to distinguish the key in a list.</summary>
    public required string KeyPrefix { get; init; }

    /// <summary>The scope set. The effective authority is <c>role ∩ scope</c>.</summary>
    public required IReadOnlyList<ApiKeyScope> Scopes { get; init; }

    /// <summary>The expiration. Never expires if <see langword="null"/>.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>The revocation timestamp. The row is NOT DELETED; the audit trail is preserved through this field.</summary>
    public DateTimeOffset? RevokedAt { get; init; }

    /// <summary>The last-used time. Used to spot an unused key.</summary>
    public DateTimeOffset? LastUsedAt { get; init; }

    /// <summary>The creation time (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary><see langword="true"/> if the key is not revoked and has not expired.</summary>
    /// <remarks>
    /// For DISPLAY ONLY (the UI list, <c>GET /api/api-keys</c>). The actual
    /// authentication decision is made in <c>ApiKeyAuthenticator</c> with an
    /// injected <see cref="TimeProvider"/>; using
    /// <see cref="DateTimeOffset.UtcNow"/> here is not a security decision
    /// and does not affect testability.
    /// </remarks>
    public bool IsActive => RevokedAt is null && (ExpiresAt is null || ExpiresAt > DateTimeOffset.UtcNow);
}

/// <summary>The result of a key creation operation.</summary>
/// <remarks>The raw key is returned ONLY here, at creation time.</remarks>
public sealed record ApiKeyCreationResult
{
    /// <summary>The saved view, which carries no raw value.</summary>
    public required ApiKeyRecord Record { get; init; }

    /// <summary>
    /// The raw key value. Cannot be produced again after this call; the
    /// consumer must show it immediately and not store it.
    /// </summary>
    public required string PlaintextKey { get; init; }

    /// <summary>Returns a description that carries no raw key.</summary>
    /// <returns>The type name and the saved record; never the raw value.</returns>
    /// <remarks>
    /// A record's compiler-generated <c>ToString</c> prints every property, and
    /// this one holds the only copy of the raw key that will ever exist.
    /// </remarks>
    public override string ToString()
        => $"{nameof(ApiKeyCreationResult)} {{ Record = {Record.Id}, PlaintextKey = [redacted] }}";
}
