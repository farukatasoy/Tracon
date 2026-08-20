namespace AgentPrism;

/// <summary>The store for per-tenant API keys.</summary>
/// <remarks>
/// A second identity source alongside the static bearer token,
/// bound to a tenant and carrying a scope; it does NOT replace the static
/// token.
/// </remarks>
public interface IApiKeyStore
{
    /// <summary>Generates and stores a new key.</summary>
    /// <param name="draft">The key's draft.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The saved record and the raw key value. The raw value cannot be
    /// produced again after this call.
    /// </returns>
    ValueTask<ApiKeyCreationResult> CreateAsync(ApiKeyDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Lists a tenant's keys. The raw value and digest are NOT returned.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The keys, by creation time.</returns>
    ValueTask<IReadOnlyList<ApiKeyRecord>> ListAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Looks up a key by its SHA-256 digest.</summary>
    /// <param name="keyHash">The digest of the presented raw value.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The record; <see langword="null"/> if it does not exist.</returns>
    /// <remarks>
    /// No tenant filter <strong>is applied</strong>: the tenant is the
    /// OUTPUT of this call, not its INPUT — while authenticating a request,
    /// which tenant it belongs to is not yet known. The lookup
    /// always goes through the digest; the raw value never enters any query directly.
    /// </remarks>
    ValueTask<ApiKeyRecord?> FindByHashAsync(ReadOnlyMemory<byte> keyHash, CancellationToken cancellationToken = default);

    /// <summary>Revokes a key. The row is NOT DELETED; <c>revoked_at</c> is written.</summary>
    /// <param name="tenantId">The tenant the key is bound to.</param>
    /// <param name="id">The key identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the key was found in this tenant and revoked.</returns>
    ValueTask<bool> RevokeAsync(string tenantId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Updates the last-used timestamp.</summary>
    /// <param name="id">The key identifier.</param>
    /// <param name="usedAt">The usage time.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// There is no tenant filter: the caller (<c>ApiKeyAuthenticator</c>)
    /// has already found the key through its digest and RESOLVED the tenant;
    /// a second check here is unnecessary — the same rationale as
    /// <see cref="FindByHashAsync"/>.
    /// </remarks>
    ValueTask TouchLastUsedAsync(Guid id, DateTimeOffset usedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports whether at least one key exists in the system (in any tenant)
    /// that carries the given scope, is not revoked, and has not expired.
    /// </summary>
    /// <param name="scope">The scope to look for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if found.</returns>
    /// <remarks>
    /// There is DELIBERATELY no tenant filter: this is an installation
    /// health check, not specific
    /// to any tenant — it asks whether the system has at least one valid
    /// <c>external:invoke</c> key, so the external surface can be opened
    /// TOGETHER with <c>AllowRemoteAccess</c>.
    /// </remarks>
    ValueTask<bool> HasActiveScopeAsync(ApiKeyScope scope, CancellationToken cancellationToken = default);
}
