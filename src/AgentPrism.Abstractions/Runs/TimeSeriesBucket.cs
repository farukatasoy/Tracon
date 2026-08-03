using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>Zaman serisi sorgusunun kova genisligi.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TimeSeriesBucket>))]
public enum TimeSeriesBucket
{
    /// <summary>Saatlik kova.</summary>
    Hour = 0,

    /// <summary>Gunluk kova.</summary>
    Day = 1,
}
