using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>The status of a queued job.</summary>
/// <remarks>
/// Written as a name in JSON, stored as <c>smallint</c> in the database. The
/// value order <strong>must not change</strong> — only append.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<JobStatus>))]
public enum JobStatus
{
    /// <summary>The job is waiting in the queue, not yet leased.</summary>
    Pending = 0,

    /// <summary>A worker has leased the job but has not started executing it yet.</summary>
    Leased = 1,

    /// <summary>The job is currently executing.</summary>
    Running = 2,

    /// <summary>The job completed successfully.</summary>
    Completed = 3,

    /// <summary>The job failed after exceeding <c>AgentPrismSchedulingOptions.MaxAttempts</c>.</summary>
    Failed = 4,

    /// <summary>The job was cancelled.</summary>
    Cancelled = 5,
}
