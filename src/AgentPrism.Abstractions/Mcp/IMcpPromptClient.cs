namespace AgentPrism;

/// <summary>Bir MCP istegin sonuc durumu.</summary>
public enum McpOperationStatus
{
    /// <summary>Istek basarili.</summary>
    Ok = 0,

    /// <summary>Bu addda kayitli bir sunucu yok.</summary>
    ServerNotFound = 1,

    /// <summary>Sunucu istenen yetenegi (<c>prompts</c>/<c>resources</c>) bildirmiyor.</summary>
    CapabilityUnsupported = 2,

    /// <summary>Sunucuya baglanilamadi veya istek sirasinda hata olustu.</summary>
    ConnectionFailed = 3,

    /// <summary>Istenen prompt/kaynak sunucuda yok.</summary>
    ItemNotFound = 4,

    /// <summary>
    /// Istenen kaynak URI'si sunucunun bildirdigi kaynak listesinde degil.
    /// Serbest URI okumasi SSRF riski tasidigi icin reddedilir.
    /// </summary>
    UriNotDeclared = 5,
}

/// <summary>Bir MCP prompt argumaninin ozeti.</summary>
public sealed record McpPromptArgumentSummary
{
    /// <summary>Arguman adi.</summary>
    public required string Name { get; init; }

    /// <summary>Aciklama.</summary>
    public string? Description { get; init; }

    /// <summary>Arguman zorunlu mu.</summary>
    public bool Required { get; init; }
}

/// <summary>Bir MCP prompt'unun ozeti.</summary>
public sealed record McpPromptSummary
{
    /// <summary>Prompt adi.</summary>
    public required string Name { get; init; }

    /// <summary>Arayuzde gosterilecek baslik.</summary>
    public string? Title { get; init; }

    /// <summary>Aciklama.</summary>
    public string? Description { get; init; }

    /// <summary>Prompt'un kabul ettigi argumanlar.</summary>
    public IReadOnlyList<McpPromptArgumentSummary> Arguments { get; init; } = [];
}

/// <summary>Prompt listeleme sonucu.</summary>
public sealed record McpPromptListResult
{
    /// <summary>Sonuc durumu.</summary>
    public required McpOperationStatus Status { get; init; }

    /// <summary>Prompt listesi. <see cref="Status"/> <see cref="McpOperationStatus.Ok"/> degilse bos.</summary>
    public IReadOnlyList<McpPromptSummary> Prompts { get; init; } = [];
}

/// <summary>
/// Bir prompt'un cozulmus icerigi.
/// </summary>
/// <remarks>
/// <strong>Anlik goruntudur.</strong> Bu icerik agent talimatina KOPYALANMALIDIR;
/// calisma aninda yeniden cekilmez. Uzak bir sunucu, agent'in davranisini bu
/// yolla degistiremez. Gerekce: docs/22-MCP-DERINLESMESI.md, bolum 22.1.
/// </remarks>
public sealed record McpPromptContent
{
    /// <summary>Prompt mesajlarindan birlestirilmis metin.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// Icerigin SHA-256 ozeti (hex). Anlik goruntu <c>AgentDefinition.Metadata</c>
    /// icinde <c>mcp.prompt.hash</c> anahtariyla saklanir; sunucudaki icerik
    /// degisince arayuz bu ozeti karsilastirip rozet gosterir.
    /// </summary>
    public required string Hash { get; init; }
}

/// <summary>Bir MCP sunucusunun prompt'larini listeleyen ve iceriklerini cozen istemci.</summary>
/// <remarks>
/// <para>
/// Yetenek denetimi zorunludur: sunucu <c>ServerCapabilities.Prompts</c> bildirmiyorsa
/// istek hic gonderilmez ve <see cref="McpOperationStatus.CapabilityUnsupported"/> doner.
/// </para>
/// <para>
/// Her cagri kisa omurlu, ayri bir baglanti kurar; <c>AgentPrism.Mcp</c>'nin arka
/// planda tuttugu tool kesif baglantisiyla paylasilmaz. Bu, bir yoneticinin
/// prompt listesini istedigi an taze goreblmesini saglar — tazeleme araligini
/// beklemez.
/// </para>
/// </remarks>
public interface IMcpPromptClient
{
    /// <summary>Bir sunucunun prompt listesini getirir.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="serverName">Sunucu adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    ValueTask<McpPromptListResult> ListPromptsAsync(
        string tenantId,
        string serverName,
        CancellationToken cancellationToken = default);

    /// <summary>Bir prompt'un icerigini argumanlarla cozer.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="serverName">Sunucu adi.</param>
    /// <param name="promptName">Prompt adi.</param>
    /// <param name="arguments">Prompt argumanlari.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    ValueTask<(McpOperationStatus Status, McpPromptContent? Content)> GetPromptAsync(
        string tenantId,
        string serverName,
        string promptName,
        IReadOnlyDictionary<string, string>? arguments,
        CancellationToken cancellationToken = default);
}
