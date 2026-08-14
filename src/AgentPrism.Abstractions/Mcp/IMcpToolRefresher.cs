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
    /// <returns>Bu tazelemenin sonucu.</returns>
    ValueTask<McpRefreshOutcome> RefreshAsync(CancellationToken cancellationToken = default);
}

/// <summary>Bir <see cref="IMcpToolRefresher.RefreshAsync"/> cagrisinin sonucu.</summary>
public readonly record struct McpRefreshOutcome
{
    /// <summary>Bu tazelemede kesfedilen toplam tool sayisi.</summary>
    public required int ToolCount { get; init; }

    /// <summary>
    /// Bu tazelemede en az bir MCP sunucusuna GERCEKTEN baglanma girisiminde
    /// bulunulup basarisiz olundu mu (zaman asimi veya aktif baglanti reddi
    /// gibi bir aglayici hatasi). Sunucunun kasitli olarak atlanmasi (gecersiz
    /// ad/adres, eksik OAuth geri donus yapilandirmasi gibi kalici bir
    /// yapilandirma sorunu) bu kapsamda DEGILDIR — o hicbir zaman "yeniden
    /// denenince duzelir" turunden bir durum degildir (HATA-006).
    /// </summary>
    public required bool HadUnreachableServers { get; init; }
}
