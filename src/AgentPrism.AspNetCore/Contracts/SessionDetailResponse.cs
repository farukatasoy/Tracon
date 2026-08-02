using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// Tek bir oturumun ayrintili gorunumu: ustveri ve sohbet gecmisi.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Messages"/>, Microsoft Agent Framework'un <c>ChatMessage</c>
/// dizisidir ve <c>Microsoft.Extensions.AI</c> serilestirme ayarlariyla
/// uretilir. AgentPrism bunun uzerine kendi paralel tip hiyerarsisini koymaz
/// (kural K3); JSON bicimi de bu yuzden MAF'in belgelenmis bicimidir.
/// </para>
/// <para>
/// <see cref="State"/> oturumun serilestirilmis halidir ve <strong>opaktir</strong>.
/// Icerigi MAF'a aittir; AgentPrism yorumlamaz.
/// </para>
/// </remarks>
public sealed record SessionDetailResponse
{
    /// <summary>Oturum kimligi.</summary>
    public required string Id { get; init; }

    /// <summary>Oturumun ait oldugu agent.</summary>
    public required string AgentName { get; init; }

    /// <summary>Kiraci kimligi.</summary>
    public string? TenantId { get; init; }

    /// <summary>Olusturulma zamani.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Son guncelleme zamani.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Sohbet gecmisi. Gecmis okunamadiysa (agent artik katalogda yoksa)
    /// <see langword="null"/> doner.
    /// </summary>
    public JsonElement? Messages { get; init; }

    /// <summary>Serilestirilmis oturum durumu.</summary>
    public required JsonElement State { get; init; }
}
