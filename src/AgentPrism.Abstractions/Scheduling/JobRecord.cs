using System.Text.Json;

namespace AgentPrism;

/// <summary>Kuyruktaki bir isin ozeti. Ogelerin (<see cref="JobItemRecord"/>) basligidir.</summary>
public sealed record JobRecord
{
    /// <summary>Is kimligi.</summary>
    public required Guid Id { get; init; }

    /// <summary>Isin ait oldugu kiraci.</summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Bu isi ureten zamanlamanin kimligi. Elle olusturulan (tek seferlik)
    /// islerde <see langword="null"/>.
    /// </summary>
    public Guid? ScheduleId { get; init; }

    /// <summary>Isin turu.</summary>
    public required JobKind Kind { get; init; }

    /// <summary>Calistirilacak agent veya workflow adi.</summary>
    public required string TargetName { get; init; }

    /// <summary>Isin guncel durumu.</summary>
    public required JobStatus Status { get; init; }

    /// <summary>Girdi kumesi veya parametreler.</summary>
    public JsonElement Payload { get; init; }

    /// <summary>Toplam oge sayisi.</summary>
    public int TotalItems { get; init; }

    /// <summary>Basariyla tamamlanan oge sayisi.</summary>
    public int DoneItems { get; init; }

    /// <summary>Basarisiz olan oge sayisi.</summary>
    public int FailedItems { get; init; }

    /// <summary>Kiralama denemesi sayisi. Her <see cref="IJobStore.LeaseAsync"/> cagrisinda artar.</summary>
    public int Attempt { get; init; }

    /// <summary>Isi su anda kiralayan iscinin kimligi. Kiralanmadiysa <see langword="null"/>.</summary>
    public string? LeaseOwner { get; init; }

    /// <summary>Mevcut kiranin sona erecegi zaman (UTC). Suresi dolarsa is yeniden kiralanabilir.</summary>
    public DateTimeOffset? LeaseUntil { get; init; }

    /// <summary>Isin calismaya uygun oldugu en erken zaman (UTC).</summary>
    public required DateTimeOffset ScheduledFor { get; init; }

    /// <summary>Ilk kiralamanin gerceklestigi zaman (UTC).</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>Bitis zamani (UTC). Is surerken <see langword="null"/>.</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Basarisizlik mesaji. Yalnizca <see cref="JobStatus.Failed"/> durumunda dolu.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Olusturulma zamani (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}
