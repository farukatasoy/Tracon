namespace AgentPrism;

/// <summary>
/// Bir skill script'ini calistirma iznini tasiyan kayit.
/// </summary>
/// <remarks>
/// <para>
/// Script calistirma, AgentPrism'in "tool'lar yalniz kodda tanimlanir" kuralinin
/// bilincli istisnasidir. Izin kaydi bu istisnanin kapisidir: kaydi olmayan bir
/// script, beyaz listedeki bir yorumlayici ile bile calismaz.
/// </para>
/// <para>
/// <see cref="ScriptName"/> <see langword="null"/> ise izin skill'in
/// <em>tum</em> script'lerini kapsar.
/// </para>
/// </remarks>
public sealed record SkillScriptGrant
{
    /// <summary>Izin kaydinin kimligi. Zaman sirali UUID (v7).</summary>
    public Guid Id { get; init; }

    /// <summary>Iznin ait oldugu kiraci.</summary>
    public required string TenantId { get; init; }

    /// <summary>Izin verilen skill'in adi.</summary>
    public required string SkillName { get; init; }

    /// <summary>
    /// Izin verilen script'in adi. <see langword="null"/> ise skill'in tum
    /// script'leri kapsanir.
    /// </summary>
    public string? ScriptName { get; init; }

    /// <summary>Izni veren aktor.</summary>
    public string? GrantedBy { get; init; }

    /// <summary>Iznin verildigi an (UTC).</summary>
    public DateTimeOffset GrantedAt { get; init; }

    /// <summary>Iznin sona erecegi an. <see langword="null"/> ise suresizdir.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Iznin geri alindigi an. <see langword="null"/> ise yururluktedir.</summary>
    public DateTimeOffset? RevokedAt { get; init; }

    /// <summary>Iznin verilen anda yururlukte olup olmadigini bildirir.</summary>
    /// <param name="instant">Degerlendirme ani.</param>
    /// <returns>Izin yururlukteyse <see langword="true"/>.</returns>
    /// <remarks>
    /// Suresi dolmus izin <strong>otomatik olarak</strong> gecersizdir; kaydin
    /// silinmesi beklenmez. Temizleme isi veri saklama fazinin konusudur.
    /// </remarks>
    public bool IsActiveAt(DateTimeOffset instant)
        => RevokedAt is null && (ExpiresAt is null || ExpiresAt > instant);
}
