using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>The status of an eval run.</summary>
/// <remarks>
/// Written as a name in JSON, stored as <c>smallint</c> in the database. The
/// value order <strong>must not change</strong> — only append.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<EvalRunStatus>))]
public enum EvalRunStatus
{
    /// <summary>The run is queued, execution has not started yet.</summary>
    Pending = 0,

    /// <summary>The run is currently executing.</summary>
    Running = 1,

    /// <summary>The run completed.</summary>
    Completed = 2,

    /// <summary>The run failed (for example, the agent or suite was not found).</summary>
    Failed = 3,

    /// <summary>The run was cancelled.</summary>
    Cancelled = 4,
}
