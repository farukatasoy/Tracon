using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>Defines the lifecycle status of an A/B experiment.</summary>
/// <remarks>
/// This value is written <strong>by name</strong> in JSON and stored as
/// <c>smallint</c> in the database. The value order cannot change.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ExperimentStatus>))]
public enum ExperimentStatus
{
    /// <summary>Created but not receiving traffic. It can be edited.</summary>
    Draft = 0,

    /// <summary>Traffic is split by weight. It cannot be edited and can only be stopped.</summary>
    Running = 1,

    /// <summary>Stopped. New sessions use the current version and history does not change.</summary>
    Stopped = 2,
}
