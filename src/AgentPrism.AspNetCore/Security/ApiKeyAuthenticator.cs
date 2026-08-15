namespace AgentPrism;

/// <summary>Resolves a presented raw token value to an API key.</summary>
/// <remarks>
/// Collects the hash &gt; lookup &gt; expiry/revocation chain in one place, so that both
/// <see cref="AgentPrismEndpointFilter"/> and a second entry point that may be added later
/// (a WebSocket handshake, for example) do not write the same logic twice.
/// </remarks>
internal static class ApiKeyAuthenticator
{
    /// <summary>Validates a raw token value and returns its record.</summary>
    /// <param name="store">The key store.</param>
    /// <param name="presentedToken">The presented raw value.</param>
    /// <param name="timeProvider">The time source used for the expiry check.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The record when the key is valid; otherwise <see langword="null"/>.</returns>
    public static async ValueTask<ApiKeyRecord?> AuthenticateAsync(
        IApiKeyStore store,
        string presentedToken,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (string.IsNullOrEmpty(presentedToken))
        {
            return null;
        }

        var hash = ApiKeyGenerator.ComputeHash(presentedToken);
        var record = await store.FindByHashAsync(hash, cancellationToken).ConfigureAwait(false);

        if (record is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();

        if (record.RevokedAt is not null || record.ExpiresAt is { } expires && expires <= now)
        {
            return null;
        }

        return record;
    }
}
