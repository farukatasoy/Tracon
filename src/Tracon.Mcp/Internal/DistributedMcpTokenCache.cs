using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Authentication;

namespace Tracon;

/// <summary>
/// A cache that keeps one tenant's OAuth token for one MCP server in the application's
/// <see cref="IDistributedCache"/>, encrypted, so every instance that shares the cache uses it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The process keeps no second copy.</strong> A refresh that a provider answers with a new
/// refresh token invalidates the old one. An instance that kept the old one in memory would refresh
/// with it and could cost the whole grant. Every <see cref="GetTokensAsync"/> reads the cache.
/// </para>
/// <para>
/// <strong>One refresh at a time.</strong> When the token that is read has expired and has a refresh
/// token, the instance takes a lease (<see cref="ISingletonLeaseStore"/>) before it returns the token to the SDK,
/// which then refreshes. The instance that does not get the lease reads the cache until the new token
/// shows up. The lease is "eventually correct, NOT strictly exclusive" (see that interface): it makes
/// the race rare, not impossible. When the wait ends without a new token, the instance returns what it
/// has, which is the behavior without the lease.
/// </para>
/// <para>
/// <strong>The key is the server's definition, not its name.</strong> The stored value carries the
/// endpoint and client id it was issued for, and a definition that differs gets no token
/// (see <see cref="McpTokenBinding"/>). A value that cannot be read, because it was written under
/// an encryption key that is gone or is not an encrypted token at all, counts as no token: the server
/// asks for authorization again.
/// </para>
/// <para>
/// A failing cache never takes the application down: a read that fails returns no token and logs, so
/// the server's tools drop out of the next refresh exactly as they do for a server that cannot be reached.
/// A write that fails throws, and the SDK ends the flow that stored the token.
/// </para>
/// </remarks>
internal sealed class DistributedMcpTokenCache : ITokenCache
{
    private readonly McpOAuthDistributedBackend _backend;
    private readonly McpTenantServerKey _key;
    private readonly McpTokenBinding _binding;
    private readonly string _cacheKey;
    private readonly string _leaseName;
    private readonly ILogger _logger;
    private readonly LeaseGate _leaseGate = new();

    private string? _leaseOwner;
    private DateTimeOffset _leaseHeldUntil;

    /// <summary>Initializes the cache of one tenant's binding to one server.</summary>
    /// <param name="backend">The distributed services.</param>
    /// <param name="key">The tenant and server.</param>
    /// <param name="binding">The definition fields the token is bound to.</param>
    /// <param name="logger">The logger.</param>
    public DistributedMcpTokenCache(
        McpOAuthDistributedBackend backend,
        McpTenantServerKey key,
        McpTokenBinding binding,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(logger);

        _backend = backend;
        _key = key;
        _binding = binding;
        _logger = logger;
        _cacheKey = McpOAuthCacheKeys.Token(key);
        _leaseName = McpOAuthCacheKeys.RefreshLease(key);
    }

    /// <inheritdoc />
    public async ValueTask<TokenContainer?> GetTokensAsync(CancellationToken cancellationToken)
    {
        var stored = await ReadAsync(cancellationToken).ConfigureAwait(false);

        if (stored is null)
        {
            return null;
        }

        var tokens = stored.ToContainer();

        if (!IsExpired(stored) || string.IsNullOrEmpty(stored.RefreshToken))
        {
            return tokens;
        }

        return await AdmitRefreshAsync(tokens, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask StoreTokensAsync(TokenContainer tokens, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        try
        {
            var sealedBytes = McpOAuthEnvelope.Seal(
                _backend.Protector,
                McpStoredTokens.From(tokens, _binding),
                McpOAuthJsonContext.Default.McpStoredTokens);

            await _backend.Cache
                .SetAsync(
                    _cacheKey,
                    sealedBytes,
                    new DistributedCacheEntryOptions { SlidingExpiration = _backend.SlidingLifetime },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            // The new token is what the other instances wait for. When the write failed they
            // stop waiting at the end of the lease instead.
            await ReleaseLeaseAsync().ConfigureAwait(false);
        }
    }

    private static bool Matches(McpStoredTokens stored, McpTokenBinding binding) => stored.Binding == binding;

    // The SDK's own rule, with no margin: it refreshes a token at ObtainedAt + ExpiresIn and not
    // before (ModelContextProtocol.Core 2.3.0, TokenContainer.IsExpired). A margin here would take
    // the lease for a token the SDK still uses, the SDK would never store a new one, and the other
    // instances would wait for it until the lease ran out.
    private bool IsExpired(McpStoredTokens stored)
        => stored.ExpiresIn is { } seconds
            && _backend.Clock.GetUtcNow() >= stored.ObtainedAt.AddSeconds(seconds);

    private async Task<McpStoredTokens?> ReadAsync(CancellationToken cancellationToken)
    {
        byte[]? bytes;

        try
        {
            bytes = await _backend.Cache.GetAsync(_cacheKey, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
        {
            _logger.LogWarning(
                ex,
                "The distributed cache could not be read; MCP server '{ServerName}' has no OAuth token until it can be.",
                _key.ServerName);

            return null;
        }

        if (bytes is null)
        {
            return null;
        }

        if (!McpOAuthEnvelope.TryOpen(_backend.Protector, bytes, McpOAuthJsonContext.Default.McpStoredTokens, out var stored))
        {
            _logger.LogWarning(
                "The OAuth token stored for MCP server '{ServerName}' cannot be read: the encryption key it was written with may be gone, or the value is not an encrypted token. Authorize the server again.",
                _key.ServerName);

            return null;
        }

        return Matches(stored!, _binding) ? stored : null;
    }

    private async ValueTask<TokenContainer> AdmitRefreshAsync(TokenContainer tokens, CancellationToken cancellationToken)
    {
        if (HoldsLease())
        {
            return tokens;
        }

        var timing = _backend.Timing;
        var owner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

        try
        {
            if (await _backend.Leases.TryAcquireAsync(_leaseName, owner, timing.RefreshLease, cancellationToken).ConfigureAwait(false))
            {
                lock (_leaseGate)
                {
                    _leaseOwner = owner;
                    _leaseHeldUntil = _backend.Clock.GetUtcNow() + timing.RefreshLease;
                }

                return tokens;
            }
        }
        catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
        {
            // No lease store means no serialization, which is what the memory mode has too.
            _logger.LogWarning(
                ex,
                "The refresh lease of MCP server '{ServerName}' could not be taken; the token is refreshed without it.",
                _key.ServerName);

            return tokens;
        }

        // Another instance refreshes. Its new token is what this one needs.
        var fresh = await McpOAuthPoll
            .UntilAsync(
                async token =>
                {
                    var read = await ReadAsync(token).ConfigureAwait(false);

                    return read is not null && !IsExpired(read) ? read : null;
                },
                timing.RefreshPoll,
                timing.RefreshWait,
                _backend.Clock,
                cancellationToken)
            .ConfigureAwait(false);

        return fresh?.ToContainer() ?? tokens;
    }

    private bool HoldsLease()
    {
        lock (_leaseGate)
        {
            return _leaseOwner is not null && _backend.Clock.GetUtcNow() < _leaseHeldUntil;
        }
    }

    private async ValueTask ReleaseLeaseAsync()
    {
        string? owner;

        lock (_leaseGate)
        {
            owner = _leaseOwner;
            _leaseOwner = null;
        }

        if (owner is null)
        {
            return;
        }

        try
        {
            await _backend.Leases.ReleaseAsync(_leaseName, owner, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The lease runs out by itself; a failed release only delays the next refresh.
            _logger.LogWarning(
                ex,
                "The refresh lease of MCP server '{ServerName}' could not be released; it ends by itself.",
                _key.ServerName);
        }
    }

    // A private type, not `object`: System.Threading.Lock does not exist on net8.0, and an
    // object field is what MA0158 flags on the others.
    private sealed class LeaseGate;
}
