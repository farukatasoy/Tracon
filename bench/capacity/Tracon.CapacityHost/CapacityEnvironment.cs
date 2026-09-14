using System.Globalization;

namespace Tracon.CapacityHost;

/// <summary>The two scheduling knobs the profile sets on the host process.</summary>
/// <remarks>
/// Separate from <see cref="CapacitySettings"/> because the scheduling callback
/// runs before the settings object is available to it, and because these two
/// values are the ones the driver re-reads at run time rather than trusting.
/// </remarks>
public static class CapacityEnvironment
{
    /// <summary>The variable that turns the host's own in-process worker off.</summary>
    public const string WorkerAxisVariable = "TRACON_CAPACITY_WORKER_AXIS";

    /// <summary>The variable that sets how many jobs one process runs at once.</summary>
    public const string MaxConcurrentJobsVariable = "TRACON_CAPACITY_MAX_JOBS";

    /// <summary>
    /// Whether the worker axis is being measured. When it is, the API host does
    /// NOT lease: the jobs are left to the separate worker processes, so the
    /// number on the axis is the number of processes that actually compete.
    /// </summary>
    public static bool WorkerAxisActive
        => string.Equals(Environment.GetEnvironmentVariable(WorkerAxisVariable), "1", StringComparison.Ordinal);

    /// <summary>How many jobs one process runs at once.</summary>
    public static int MaxConcurrentJobs
        => int.TryParse(
            Environment.GetEnvironmentVariable(MaxConcurrentJobsVariable),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value) && value > 0
            ? value
            : 8;
}
