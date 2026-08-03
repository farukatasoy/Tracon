namespace AgentPrism;

/// <summary>Bir MCP kaynaginin ozeti.</summary>
public sealed record McpResourceSummary
{
    /// <summary>Kaynak URI'si.</summary>
    public required string Uri { get; init; }

    /// <summary>Kaynak adi.</summary>
    public required string Name { get; init; }

    /// <summary>MIME turu.</summary>
    public string? MimeType { get; init; }

    /// <summary>Aciklama.</summary>
    public string? Description { get; init; }
}

/// <summary>Kaynak listeleme sonucu.</summary>
public sealed record McpResourceListResult
{
    /// <summary>Sonuc durumu.</summary>
    public required McpOperationStatus Status { get; init; }

    /// <summary>Kaynak listesi. <see cref="Status"/> <see cref="McpOperationStatus.Ok"/> degilse bos.</summary>
    public IReadOnlyList<McpResourceSummary> Resources { get; init; } = [];
}

/// <summary>Okunmus bir kaynagin icerigi.</summary>
public sealed record McpResourceContent
{
    /// <summary>Kaynak URI'si.</summary>
    public required string Uri { get; init; }

    /// <summary>MIME turu.</summary>
    public string? MimeType { get; init; }

    /// <summary>Metin icerigi. Ikili kaynaklarda <see langword="null"/>.</summary>
    public string? Text { get; init; }

    /// <summary>Kaynak ikili mi (blob).</summary>
    public bool IsBinary { get; init; }

    /// <summary>Ham icerigin bayt boyutu (kirpmadan once).</summary>
    public int ByteSize { get; init; }

    /// <summary>
    /// Icerik boyut siniri asildigi icin kirpildi mi (docs/22-MCP-DERINLESMESI.md, bolum 22.2).
    /// </summary>
    public bool Truncated { get; init; }
}

/// <summary>Bir MCP sunucusunun kaynaklarini listeleyen ve okuyan istemci.</summary>
/// <remarks>
/// <para>
/// Yetenek denetimi zorunludur: sunucu <c>ServerCapabilities.Resources</c>
/// bildirmiyorsa istek hic gonderilmez.
/// </para>
/// <para>
/// <strong>Yalniz bildirilen URI'ler okunabilir.</strong> <see cref="ReadResourceAsync"/>
/// oncelikle sunucunun <c>ListResourcesAsync</c> ile bildirdigi kume ile
/// karsilastirir; kumede olmayan bir URI <see cref="McpOperationStatus.UriNotDeclared"/>
/// ile reddedilir. Aksi hâlde bu bir SSRF araci olurdu (docs/22-MCP-DERINLESMESI.md, bolum 22.2).
/// </para>
/// </remarks>
public interface IMcpResourceClient
{
    /// <summary>Bir sunucunun kaynak listesini getirir.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="serverName">Sunucu adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    ValueTask<McpResourceListResult> ListResourcesAsync(
        string tenantId,
        string serverName,
        CancellationToken cancellationToken = default);

    /// <summary>Bir kaynagi okur.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="serverName">Sunucu adi.</param>
    /// <param name="uri">Okunacak kaynagin URI'si; sunucunun bildirdigi kumede olmalidir.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    ValueTask<(McpOperationStatus Status, McpResourceContent? Content)> ReadResourceAsync(
        string tenantId,
        string serverName,
        string uri,
        CancellationToken cancellationToken = default);
}
