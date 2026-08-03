using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>Bir A/B deneyinin yasam dongusu durumu.</summary>
/// <remarks>
/// JSON'da <strong>ad olarak</strong> yazilir; veritabaninda <c>smallint</c> olarak
/// saklanir. Deger sirasi degistirilemez.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ExperimentStatus>))]
public enum ExperimentStatus
{
    /// <summary>Olusturuldu, henuz trafik almiyor. Duzenlenebilir.</summary>
    Draft = 0,

    /// <summary>Trafik agirliklara gore bolunuyor. Duzenlenemez; sadece durdurulabilir.</summary>
    Running = 1,

    /// <summary>Durduruldu. Yeni oturumlar guncel surume gider; gecmis degismez.</summary>
    Stopped = 2,
}
