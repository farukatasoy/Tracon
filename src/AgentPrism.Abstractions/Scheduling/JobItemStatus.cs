using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>Bir toplu is ogesinin durumu.</summary>
/// <remarks>
/// JSON'da ad olarak yazilir, veritabaninda <c>smallint</c> olarak saklanir.
/// Deger sirasi <strong>degistirilemez</strong> — yalnizca sona eklenir.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<JobItemStatus>))]
public enum JobItemStatus
{
    /// <summary>Oge henuz islenmedi.</summary>
    Pending = 0,

    /// <summary>Oge basariyla islendi.</summary>
    Completed = 1,

    /// <summary>Oge islenirken hata olustu.</summary>
    Failed = 2,
}
