using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>The status of a batch job item.</summary>
/// <remarks>
/// Written as a name in JSON, stored as <c>smallint</c> in the database. The
/// value order <strong>must not change</strong> — only append.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<JobItemStatus>))]
public enum JobItemStatus
{
    /// <summary>The item has not been processed yet.</summary>
    Pending = 0,

    /// <summary>The item was processed successfully.</summary>
    Completed = 1,

    /// <summary>An error occurred while processing the item.</summary>
    Failed = 2,
}
