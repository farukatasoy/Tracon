namespace AgentPrism;

/// <summary>Batch and scheduled run settings.</summary>
/// <remarks>
/// Read from the <c>AgentPrism:Scheduling</c> configuration section. See
/// <c>AgentPrismServiceCollectionExtensions.UseScheduling</c>.
/// </remarks>
public sealed class AgentPrismSchedulingOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "AgentPrism:Scheduling";

    /// <summary>Whether the scheduling subsystem is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether the background worker runs in this process. If set to
    /// <see langword="false"/>, the queue and schedule stores keep working,
    /// but no job is leased or executed in this process — distribution is
    /// left to another process.
    /// </summary>
    public bool RunWorker { get; set; } = true;

    /// <summary>The maximum number of jobs that may run concurrently in this process.</summary>
    public int MaxConcurrentJobs { get; set; } = 2;

    /// <summary>How often new jobs and due schedules are looked for.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>A job's lease duration. If the worker does not finish within this time, the job may be re-leased.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The maximum number of attempts a job may make before becoming <see cref="JobStatus.Failed"/>.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>The maximum number of items allowed in a single job.</summary>
    public int MaxItemsPerJob { get; set; } = 1000;
}
