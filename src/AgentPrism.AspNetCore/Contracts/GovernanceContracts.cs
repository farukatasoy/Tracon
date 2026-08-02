namespace AgentPrism;

/// <summary>Gecerli istegin kiracisi.</summary>
public sealed record CurrentTenantResponse
{
    /// <summary>Kiraci kimligi.</summary>
    public required string TenantId { get; init; }
}

/// <summary>Kiraci kaydi olusturma ve guncelleme istegi.</summary>
public sealed record TenantRequest
{
    /// <summary>Arayuzde gosterilecek ad. Bos birakilirsa anahtar kullanilir.</summary>
    public string? DisplayName { get; init; }
}

/// <summary>
/// MCP sunucusu olusturma ve guncelleme istegi.
/// </summary>
/// <remarks>
/// <strong>Sir alani yoktur.</strong> Kimlik dogrulama basliginin degeri
/// gonderilmez; yalnizca degerin okunacagi yapilandirma anahtarinin adi
/// (<see cref="AuthorizationConfigurationKey"/>) gonderilir. Deger calisma
/// aninda <c>IConfiguration</c> uzerinden cozulur ve veritabanina hicbir zaman
/// yazilmaz. Gerekce: <c>docs/KARARLAR.md</c>, karar K-059.
/// </remarks>
public sealed record McpServerRequest
{
    /// <summary>Aciklama.</summary>
    public string? Description { get; init; }

    /// <summary>Sunucu adresi. Yalnizca <c>http</c> ve <c>https</c> kabul edilir.</summary>
    public required string Endpoint { get; init; }

    /// <summary>Aktarim bicimi.</summary>
    public McpTransportMode Transport { get; init; }

    /// <summary>
    /// <c>Authorization</c> basliginin degerinin okunacagi yapilandirma anahtari.
    /// Ornek: <c>AgentPrism:Mcp:GithubToken</c>.
    /// </summary>
    public string? AuthorizationConfigurationKey { get; init; }

    /// <summary>
    /// Ek istek basliklari. <strong>Sir tasimamalidir</strong> — bu degerler
    /// oldugu gibi saklanir ve listeleme ucunda gorunur.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>Sunucu etkin mi.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Bu sunucunun tool'lari onay ister mi. Varsayilan <see langword="true"/>.</summary>
    public bool RequiresApproval { get; init; } = true;
}

/// <summary>MCP tool tazeleme sonucu.</summary>
public sealed record McpRefreshResponse
{
    /// <summary>Tazeleme sonrasi kullanilabilir toplam tool sayisi.</summary>
    public required int ToolCount { get; init; }
}

/// <summary>
/// Arayuzden gonderilen tool onay karari.
/// </summary>
/// <remarks>
/// Karar, bir sonraki calistirma isteginin govdesinde tasinir. Microsoft Agent
/// Framework onay yanitini bir <c>ChatMessage</c> icerigi olarak bekler; ayri
/// bir "devam et" ucu yoktur, cunku onay bir sonraki turun girdisidir.
/// </remarks>
public sealed record ToolApprovalDecision
{
    /// <summary>
    /// Onaylanan istegin kimligi. Akista gelen
    /// <c>ToolApprovalRequestContent.RequestId</c> degeridir.
    /// </summary>
    public required string RequestId { get; init; }

    /// <summary>Cagri onaylandi mi.</summary>
    public required bool Approved { get; init; }

    /// <summary>Karar gerekcesi. Modele iletilir.</summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Karar kalici bir kural olarak kaydedilsin mi ("bir daha sorma").
    /// Yalnizca <see cref="Approved"/> <see langword="true"/> iken anlamlidir.
    /// </summary>
    public bool Remember { get; init; }

    /// <summary>
    /// Kalici kural yalnizca ayni argumanlarla yapilan cagriyi mi kapsasin.
    /// <see langword="false"/> ise tool'un her cagrisini kapsar.
    /// </summary>
    public bool RememberArgumentsOnly { get; init; }
}
