namespace Tracon;

/// <summary>
/// What the job worker needs to know about tenants, in one place: the ceiling a tenant may hold in the queue and the
/// scope the host opens for a tenant's job.
/// </summary>
/// <remarks>
/// The worker's constructor is already wide, and both parts are optional registrations the container answers with
/// <see langword="null"/> when nobody made one. Taking them together keeps the constructor from growing a parameter for
/// each tenant concern.
/// </remarks>
/// <param name="CapSource">Names the per-tenant ceiling; <see langword="null"/> leases in plain age order.</param>
/// <param name="HostScope">Opens the host's own tenant scope around a job; <see langword="null"/> opens none.</param>
internal sealed record JobTenantBindings(IJobTenantCapSource? CapSource, IHostTenantScope? HostScope);
