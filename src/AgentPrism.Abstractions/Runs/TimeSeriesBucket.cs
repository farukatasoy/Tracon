using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>The bucket width for a time-series query.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TimeSeriesBucket>))]
public enum TimeSeriesBucket
{
    /// <summary>Hourly bucket.</summary>
    Hour = 0,

    /// <summary>Daily bucket.</summary>
    Day = 1,
}
