using ModelContextProtocol.Authentication;

namespace Tracon;

/// <summary>
/// A cache that keeps an MCP server's OAuth tokens in process memory.
/// </summary>
/// <remarks>
/// Tokens are never written to the database. This instance is shared by both the
/// interactive authorization flow (<see cref="McpOAuthAuthorizationCoordinator"/>)
/// and the background reconnection (<see cref="McpToolCatalog"/>) for the
/// same <c>(tenant, server)</c> pair; a token obtained once is thus reused
/// for the lifetime of the process.
/// </remarks>
internal sealed class InMemoryMcpTokenCache : ITokenCache, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TokenContainer? _tokens;

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    /// <inheritdoc />
    public async ValueTask<TokenContainer?> GetTokensAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return _tokens;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask StoreTokensAsync(TokenContainer tokens, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _tokens = tokens;
        }
        finally
        {
            _gate.Release();
        }
    }
}
