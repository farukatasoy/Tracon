namespace Tracon;

/// <summary>
/// Supplies the per-tenant lease ceiling the job worker passes to <see cref="IJobStore.LeaseAsync"/>.
/// </summary>
/// <remarks>
/// <c>internal</c> because the ceiling belongs to the package that owns the work: the workflow engine
/// (<c>Tracon.Workflows</c>) knows which handler keys carry workflow runs and how many of them one tenant
/// may hold, and the worker (<c>Tracon.Core</c>) cannot see that package. A host without the engine
/// registers no source, and the worker leases with no ceiling.
/// </remarks>
internal interface IJobTenantCapSource
{
    /// <summary>Gets the ceiling for the next lease, or <see langword="null"/> when none applies.</summary>
    JobTenantCap? Current { get; }
}
