using System.Globalization;

namespace Tracon.CapacityHost;

/// <summary>The knobs the profile sets on the host process through its environment.</summary>
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

    /// <summary>The variable that opens tenancy to any well-formed tenant id instead of the two fixed ones.</summary>
    public const string OpenTenantsVariable = "TRACON_CAPACITY_OPEN_TENANTS";

    /// <summary>The variable that turns the tenant tag on the run, cost and job metrics on.</summary>
    public const string TenantTagVariable = "TRACON_CAPACITY_TENANT_TAG";

    /// <summary>The variable that turns Tracon's own spans and metrics off (<c>off</c>); anything else leaves them on, as the product ships.</summary>
    public const string ObservabilityVariable = "TRACON_CAPACITY_OBSERVABILITY";

    /// <summary>
    /// Whether Tracon's observability is switched off for this run. A diagnosis, not a configuration to measure
    /// under: it separates what the telemetry path keeps from what the rest of the host keeps.
    /// </summary>
    public static bool ObservabilityOff
        => string.Equals(Environment.GetEnvironmentVariable(ObservabilityVariable), "off", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether any well-formed tenant id is accepted. Off, the host admits only the two tenants of the
    /// ordinary profiles; on, it behaves as a host with an empty allow list, which is the shape a tenant ramp needs.
    /// </summary>
    public static bool OpenTenants
        => string.Equals(Environment.GetEnvironmentVariable(OpenTenantsVariable), "1", StringComparison.Ordinal);

    /// <summary>Whether the per-tenant tag on Tracon's own metrics is on (off by default in the product).</summary>
    public static bool TenantTag
        => string.Equals(Environment.GetEnvironmentVariable(TenantTagVariable), "1", StringComparison.Ordinal);

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
