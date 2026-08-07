using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>Bir <see cref="EvalCase"/>'in uretim calistirmasindan terfi sebebi.</summary>
/// <remarks>
/// JSON'da ad olarak yazilir, veritabaninda <c>smallint</c> olarak saklanir.
/// Deger sirasi <strong>degistirilemez</strong> — yalnizca sona eklenir.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<EvalCaseSource>))]
public enum EvalCaseSource
{
    /// <summary>Basarisiz (<see cref="RunStatus.Failed"/>) bir calistirmadan terfi edildi.</summary>
    FailedRun = 0,

    /// <summary>Olumsuz puanlanmis bir calistirmadan terfi edildi.</summary>
    NegativeScore = 1,

    /// <summary>Basarili bir calistirmadan, referans olarak terfi edildi.</summary>
    ReferenceRun = 2,
}
