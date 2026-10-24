using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Gives the job worker the per-tenant lease ceiling for queued workflow work.
/// </summary>
/// <remarks>
/// The ceiling is <see cref="TraconWorkflowOptions.MaxConcurrentRunsPerTenant"/> over the two handler
/// keys that carry a workflow run (<see cref="JobHandlerKeys.Workflow"/> and
/// <see cref="JobHandlerKeys.WorkflowRun"/>). With the setting off, or the engine off, there is none
/// and the queue leases in plain age order. Read once: the process limit it mirrors is also fixed at
/// startup.
/// </remarks>
/// <param name="options">The workflow settings.</param>
internal sealed class WorkflowJobTenantCapSource(IOptions<TraconWorkflowOptions> options) : IJobTenantCapSource
{
    /// <inheritdoc />
    public JobTenantCap? Current { get; } = options.Value is { Enabled: true, MaxConcurrentRunsPerTenant: { } perTenant }
        ? new JobTenantCap { HandlerKeys = [JobHandlerKeys.Workflow, JobHandlerKeys.WorkflowRun], MaxLeasedPerTenant = perTenant }
        : null;
}
