namespace AgentPrism;

/// <summary>
/// Kim, ne zaman, hangi varligi degistirdigini kaydeden bir denetim izi satiri.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Before"/> ve <see cref="After"/> yazilmadan once sir suzgecinden
/// gecer: <c>apiKey</c>, <c>authorization</c>, <c>token</c>, <c>password</c>,
/// <c>secret</c> anahtarlarinin degerleri <c>"***"</c> ile degistirilir.
/// </para>
/// <para>
/// Calistirmalar (agent'in bir mesaji islemesi) bu deftere <strong>yazilmaz</strong>.
/// <c>runs</c> tablosu zaten tam kaydi tutar; ikinci kez yazmak denetim izini
/// en hacimli tabloya cevirir ve okunmaz hale getirir.
/// </para>
/// </remarks>
public sealed record AuditEntry
{
    /// <summary>Kayit kimligi. Zaman sirali UUID (v7).</summary>
    public required Guid Id { get; init; }

    /// <summary>Degisikligin ait oldugu kiraci.</summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Degisikligi yapan aktor. Kimlik dogrulamasi yoksa veya aktor
    /// cozulemiyorsa <see langword="null"/>'dur; bu durum gizlenmez.
    /// </summary>
    public string? Actor { get; init; }

    /// <summary>Eylem adi. Ornek: <c>agent.update</c>.</summary>
    public required string Action { get; init; }

    /// <summary>Etkilenen varlik. Ornek: <c>agent:support</c>.</summary>
    public required string Entity { get; init; }

    /// <summary>Degisiklikten onceki durum, JSON metni. Sir suzgecinden gecmistir.</summary>
    public string? Before { get; init; }

    /// <summary>Degisiklikten sonraki durum, JSON metni. Sir suzgecinden gecmistir.</summary>
    public string? After { get; init; }

    /// <summary>Kayit zamani (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}
