using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>The bucket width for a time-series query.</summary>
[Description("The bucket width of a time-series query: an hour or a day.")]
[JsonConverter(typeof(JsonStringEnumConverter<TimeSeriesBucket>))]
public enum TimeSeriesBucket
{
    /// <summary>Hourly bucket.</summary>
    Hour = 0,

    /// <summary>Daily bucket.</summary>
    Day = 1,
}
