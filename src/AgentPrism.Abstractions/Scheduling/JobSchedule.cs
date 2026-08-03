using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// Bir isin ne zaman ve nasil calisacagini tanimlayan zamanlama kaydi.
/// </summary>
/// <remarks>
/// <see cref="Cron"/> bos birakilirsa zamanlama yalnizca elle
/// (<c>POST .../trigger</c>) tetiklenir; otomatik bir sonraki calisma zamani
/// hesaplanmaz.
/// </remarks>
public sealed record JobSchedule
{
    /// <summary>Zamanlama kimligi.</summary>
    public Guid Id { get; init; }

    /// <summary>Zamanlamanin ait oldugu kiraci.</summary>
    public required string TenantId { get; init; }

    /// <summary>Kiraci icinde benzersiz zamanlama adi.</summary>
    public required string Name { get; init; }

    /// <summary>Bu zamanlamanin urettigi isin turu.</summary>
    public required JobKind Kind { get; init; }

    /// <summary>Calistirilacak agent veya workflow adi.</summary>
    public required string TargetName { get; init; }

    /// <summary>
    /// Bes alanli cron ifadesi (<c>dakika saat ayin-gunu ay haftanin-gunu</c>).
    /// <see langword="null"/> ise zamanlama yalnizca elle tetiklenir.
    /// </summary>
    public string? Cron { get; init; }

    /// <summary><see cref="Cron"/> ifadesinin yorumlandigi saat dilimi.</summary>
    public string TimeZone { get; init; } = "UTC";

    /// <summary>Girdi kumesi veya parametreler. Is turune gore yorumlanir.</summary>
    public JsonElement Payload { get; init; }

    /// <summary>Zamanlama etkin mi. Kapatilirsa otomatik tetiklenmez.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Bir sonraki otomatik calisma zamani (UTC). Cron yoksa <see langword="null"/>.</summary>
    public DateTimeOffset? NextRunAt { get; init; }

    /// <summary>Son calisma zamani (UTC). Hic calismadiysa <see langword="null"/>.</summary>
    public DateTimeOffset? LastRunAt { get; init; }

    /// <summary>Zamanlamayi olusturan kullanici/servis kimligi.</summary>
    public string? CreatedBy { get; init; }

    /// <summary>Olusturulma zamani (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Son guncelleme zamani (UTC).</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}
