using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// Bir workflow yurutmesinin tek bir kontrol noktasi.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 <see cref="State"/> <strong>opak</strong> bir yuktur. Icerigi Microsoft
/// Agent Framework'e aittir ve polimorfiktir: icinde <c>$type</c> ayraci tasir
/// ve o ayrac bulundugu nesnenin <em>ilk</em> ozelligi olmak zorundadir.
/// Olculdu (Faz 15): 7,5 KB'lik bir kontrol noktasinda <c>{"$type":0,...}</c>
/// ayraci gercekten bulunuyor.
/// </para>
/// <para>
/// Bu yuzden deger PostgreSQL'de <c>json</c> sutununda saklanir, <c>jsonb</c>
/// sutununda degil: <c>jsonb</c> anahtarlari yeniden siralar ve ayraci ilk
/// olmaktan cikarir. Karar K-027.
/// </para>
/// </remarks>
public sealed record WorkflowCheckpointRecord
{
    /// <summary>Kayit kimligi. Zaman sirali UUID (v7).</summary>
    public required Guid Id { get; init; }

    /// <summary>Kaydin ait oldugu kiraci.</summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Yurutme oturumunun kimligi. Microsoft Agent Framework kontrol noktalarini
    /// bu deger altinda gruplar.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Kontrol noktasinin kimligi. Deger Microsoft Agent Framework'e degil
    /// AgentPrism'e aittir: <c>CreateAsync</c> uretir ve MAF'a geri verir.
    /// </summary>
    public required string CheckpointId { get; init; }

    /// <summary>Bir onceki kontrol noktasinin kimligi. Ilk noktada <see langword="null"/>.</summary>
    public string? ParentCheckpointId { get; init; }

    /// <summary>
    /// Bu noktayi ureten calistirmanin kimligi. Kontrol noktasi yazildiginda
    /// suren bir calistirma yoksa <see langword="null"/>.
    /// </summary>
    public Guid? RunId { get; init; }

    /// <summary>Olusturulma zamani (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Opak yurutme durumu.</summary>
    public required JsonElement State { get; init; }
}
