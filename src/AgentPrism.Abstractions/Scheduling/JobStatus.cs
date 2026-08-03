using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>Bir kuyruk isinin durumu.</summary>
/// <remarks>
/// JSON'da ad olarak yazilir, veritabaninda <c>smallint</c> olarak saklanir.
/// Deger sirasi <strong>degistirilemez</strong> — yalnizca sona eklenir.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<JobStatus>))]
public enum JobStatus
{
    /// <summary>Is kuyrukta bekliyor, henuz kiralanmadi.</summary>
    Pending = 0,

    /// <summary>Bir isci is uzerinde kira aldi ama yururtmeye henuz baslamadi.</summary>
    Leased = 1,

    /// <summary>Is su anda yurutuluyor.</summary>
    Running = 2,

    /// <summary>Is basariyla tamamlandi.</summary>
    Completed = 3,

    /// <summary>Is, <see cref="AgentPrismSchedulingOptions.MaxAttempts"/> asilarak basarisiz oldu.</summary>
    Failed = 4,

    /// <summary>Is iptal edildi.</summary>
    Cancelled = 5,
}
