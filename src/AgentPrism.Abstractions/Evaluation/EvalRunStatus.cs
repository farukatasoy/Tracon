using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>Bir eval kosusunun durumu.</summary>
/// <remarks>
/// JSON'da ad olarak yazilir, veritabaninda <c>smallint</c> olarak saklanir.
/// Deger sirasi <strong>degistirilemez</strong> — yalnizca sona eklenir.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<EvalRunStatus>))]
public enum EvalRunStatus
{
    /// <summary>Kosu kuyruga alindi, henuz yurutmeye baslamadi.</summary>
    Pending = 0,

    /// <summary>Kosu su anda yurutuluyor.</summary>
    Running = 1,

    /// <summary>Kosu tamamlandi.</summary>
    Completed = 2,

    /// <summary>Kosu basarisiz oldu (ornegin agent veya takim bulunamadi).</summary>
    Failed = 3,

    /// <summary>Kosu iptal edildi.</summary>
    Cancelled = 4,
}
