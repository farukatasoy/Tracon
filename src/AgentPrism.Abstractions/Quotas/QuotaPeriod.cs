using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>The interval at which a quota's counter resets.</summary>
/// <remarks>
/// Written as a name in JSON, stored as <c>smallint</c> in the database. The
/// value order <strong>must not change</strong> — only append; existing rows
/// reference the numeric value.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<QuotaPeriod>))]
public enum QuotaPeriod
{
    /// <summary>The counter resets every day at local midnight.</summary>
    Daily = 0,

    /// <summary>The counter resets on the first day of every month at local midnight.</summary>
    Monthly = 1,
}
