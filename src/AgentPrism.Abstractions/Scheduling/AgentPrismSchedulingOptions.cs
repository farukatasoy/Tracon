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

    /// <summary>
    /// The lanes this worker leases from. <see langword="null"/> (the
    /// default) leases from every lane — the same behavior as before lanes
    /// existed.
    /// </summary>
    /// <remarks>
    /// A worker that only wants to run a specific lane's jobs in a dedicated
    /// process sets this instead of registering a second
    /// <c>JobWorkerBackgroundService</c>: the background service is a single
    /// <c>TryAddEnumerable</c> registration keyed by implementation type, so a
    /// second host process with its own <see cref="Lanes"/> value is the
    /// supported way to dedicate capacity, not a second in-process worker.
    /// </remarks>
    public IReadOnlyList<string>? Lanes { get; set; }

    /// <summary>
    /// Per-lane concurrency limits. A lane not listed here shares the common
    /// <see cref="MaxConcurrentJobs"/> budget.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sum of these values is allowed to exceed <see cref="MaxConcurrentJobs"/>:
    /// each listed lane gets its own dedicated budget, on top of the shared
    /// one that unlisted lanes still draw from. There is no combined cap.
    /// </para>
    /// <para>
    /// If <see cref="Lanes"/> is left <see langword="null"/> (unrestricted)
    /// while this dictionary is non-empty, the worker's effective coverage
    /// narrows to <see cref="JobLanes.Default"/> plus the lanes listed here —
    /// it does <strong>not</strong> also lease from an arbitrary lane nobody
    /// configured a budget for. This keeps the "which lanes get how much
    /// capacity" question answerable from this dictionary alone; a job queued
    /// under an unlisted, non-default lane simply is not leased by this
    /// worker (it stays visible in <c>GET /api/jobs?lane=</c> and the jobs
    /// screen). Set <see cref="Lanes"/> explicitly to include such a lane.
    /// </para>
    /// </remarks>
    public IDictionary<string, int> MaxConcurrentJobsPerLane { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    /// Maps a job kind to the lane it is queued under, when the caller did not
    /// set <see cref="JobRecord.Lane"/>/<see cref="JobSchedule.Lane"/> explicitly.
    /// </summary>
    /// <remarks>
    /// Applied once, inside <see cref="IJobStore.EnqueueAsync"/>: a job whose
    /// <c>Lane</c> is still <see cref="JobLanes.Default"/> at enqueue time is
    /// routed through this map by its <see cref="JobRecord.Kind"/>. This lets
    /// an operator isolate a built-in kind (for example
    /// <see cref="JobKind.Retention"/>) into its own lane without changing any
    /// call site that creates jobs of that kind.
    /// </remarks>
    public IDictionary<JobKind, string> LaneByKind { get; } = new Dictionary<JobKind, string>();
}
