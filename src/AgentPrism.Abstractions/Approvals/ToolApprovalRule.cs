namespace AgentPrism;

/// <summary>
/// Kullanicinin "bir daha sorma" dedigi bir tool cagrisi icin kalici onay kurali.
/// </summary>
/// <remarks>
/// <para>
/// Kural <strong>kiraci + agent + tool</strong> ucglusune baglidir. Bir kiracinin
/// verdigi onay baska bir kiracida gecerli degildir; bu bir guvenlik sinirdir ve
/// gevsetilmez.
/// </para>
/// <para>
/// <see cref="ArgumentsHash"/> dolu ise kural yalnizca <em>ayni argumanlarla</em>
/// yapilan cagriyi kapsar. Bos ise tool'un her cagrisini kapsar. Ayrimin sebebi
/// Microsoft Agent Framework'un iki ayri "her zaman onayla" bicimi sunmasidir:
/// <c>CreateAlwaysApproveToolResponse</c> ve
/// <c>CreateAlwaysApproveToolWithArgumentsResponse</c>.
/// </para>
/// </remarks>
public sealed record ToolApprovalRule
{
    /// <summary>Kural kimligi. Zaman sirali UUID (v7).</summary>
    public required Guid Id { get; init; }

    /// <summary>Kuralin gecerli oldugu kiraci.</summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Kuralin gecerli oldugu agent. <see langword="null"/> ise kiracinin
    /// tum agent'larini kapsar.
    /// </summary>
    public string? AgentName { get; init; }

    /// <summary>Kuralin gecerli oldugu tool.</summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// Arguman parmak izi. Dolu ise kural yalnizca ayni argumanlarla yapilan
    /// cagriyi kapsar.
    /// </summary>
    public string? ArgumentsHash { get; init; }

    /// <summary>Kurali kim olusturdu. Kimlik dogrulamasi yoksa <see langword="null"/>.</summary>
    public string? CreatedBy { get; init; }

    /// <summary>Olusturulma zamani (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Kalici onay kurallarinin deposu.</summary>
/// <remarks>
/// Kurallar her calistirmada okunur; uygulamalar okumayi ucuz tutmalidir.
/// Bellek ici uygulama tum kurallari bellekte tasir; PostgreSQL uygulamasi
/// <c>(tenant_id, tool_name)</c> uzerinde indekslidir.
/// </remarks>
public interface IToolApprovalRuleStore
{
    /// <summary>Bir kiracinin kurallarini listeler.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kurallar; en yeni basta.</returns>
    ValueTask<IReadOnlyList<ToolApprovalRule>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir kural ekler. Ayni ucglu ve arguman parmak izi zaten kayitliysa
    /// mevcut kural dondurulur ve yeni kayit acilmaz.
    /// </summary>
    /// <param name="rule">Eklenecek kural.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kalici kural.</returns>
    ValueTask<ToolApprovalRule> AddAsync(ToolApprovalRule rule, CancellationToken cancellationToken = default);

    /// <summary>Bir kurali siler.</summary>
    /// <param name="tenantId">Kiraci kimligi. Baska kiracinin kurali silinemez.</param>
    /// <param name="ruleId">Kural kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kural silindiyse <see langword="true"/>.</returns>
    ValueTask<bool> DeleteAsync(string tenantId, Guid ruleId, CancellationToken cancellationToken = default);
}
