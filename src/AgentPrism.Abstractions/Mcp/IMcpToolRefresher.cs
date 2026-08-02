namespace AgentPrism;

/// <summary>
/// Uzak MCP sunucularinin tool listesini istege bagli olarak tazeler.
/// </summary>
/// <remarks>
/// <para>
/// Tazeleme normalde arka planda, belirli araliklarla yapilir. Bu arayuz, bir
/// sunucu <em>yeni eklendiginde</em> kullanicinin bir sonraki tazelemeyi
/// beklemek zorunda kalmamasi icindir.
/// </para>
/// <para>
/// Soyutlama <c>AgentPrism.Abstractions</c> icindedir cunku HTTP katmani
/// tazelemeyi tetikler ancak <c>AgentPrism.Mcp</c> paketine bagli degildir:
/// MCP istegde bagli bir paket olarak kalir.
/// </para>
/// </remarks>
public interface IMcpToolRefresher
{
    /// <summary>Tool listesini simdi tazeler.</summary>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kesfedilen toplam tool sayisi.</returns>
    ValueTask<int> RefreshAsync(CancellationToken cancellationToken = default);
}
