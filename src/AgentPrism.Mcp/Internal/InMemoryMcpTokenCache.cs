using ModelContextProtocol.Authentication;

namespace AgentPrism;

/// <summary>
/// Bir MCP sunucusunun OAuth token'larini surec bellegi icinde tutan onbellek.
/// </summary>
/// <remarks>
/// Token'lar hicbir zaman veritabanina yazilmaz (docs/22-MCP-DERINLESMESI.md,
/// bolum 22.3, karar #3). Bu ornek, ayni <c>(kiraci, sunucu)</c> ciftinin hem
/// etkilesimli yetkilendirme akisi (<see cref="McpOAuthAuthorizationCoordinator"/>)
/// hem arka plan yeniden baglanmasi (<see cref="McpToolCatalog"/>) tarafindan
/// paylasilir; boylece bir kez alinan token, surec calisirken tekrar kullanilir.
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
