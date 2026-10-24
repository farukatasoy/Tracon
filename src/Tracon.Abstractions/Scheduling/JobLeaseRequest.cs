namespace Tracon;

/// <summary>
/// What a worker asks of <see cref="IJobStore.LeaseAsync"/>: who it is, how long the lease
/// lasts, which lanes it serves and, optionally, a per-tenant ceiling.
/// </summary>
/// <remarks>
/// A record, so a new option is a new property with a default and not a new parameter on
/// the interface method that every <see cref="IJobStore"/> implementation overrides.
/// </remarks>
public sealed record JobLeaseRequest
{
    /// <summary>Gets the leasing worker's identifier.</summary>
    public required string Owner { get; init; }

    /// <summary>Gets the lease's validity duration.</summary>
    public required TimeSpan LeaseDuration { get; init; }

    /// <summary>
    /// Gets the lanes the lease is restricted to (see <see cref="JobLanes"/>).
    /// <see langword="null"/> or empty applies no filter: a job from any lane may be leased.
    /// </summary>
    public IReadOnlyList<string>? Lanes { get; init; }

    /// <summary>
    /// Gets the per-tenant ceiling, or <see langword="null"/> (the default) for none: the
    /// oldest ready job is leased whichever tenant it belongs to.
    /// </summary>
    public JobTenantCap? TenantCap { get; init; }
}

/// <summary>
/// A ceiling on how many jobs of a set of handler keys one tenant may hold leased at the
/// same time.
/// </summary>
/// <remarks>
/// <para>
/// The store counts the tenant's jobs whose handler key is in <see cref="HandlerKeys"/> and
/// whose lease is alive (<see cref="JobStatus.Leased"/> or <see cref="JobStatus.Running"/>
/// with a lease that has not expired), across every worker that shares the store. A tenant
/// at the ceiling has its jobs of those keys skipped, and the oldest job that is still
/// eligible is leased instead. A job of another handler key, or of another tenant, is never
/// affected.
/// </para>
/// <para>
/// <strong>The ceiling is soft.</strong> Two workers that lease at the same moment can both
/// read a count below the ceiling, so the tenant can exceed it by up to the number of
/// workers leasing at once. It is a fairness limit that keeps one tenant from holding the
/// whole queue, not a quota.
/// </para>
/// </remarks>
public sealed record JobTenantCap
{
    /// <summary>Gets the handler keys the ceiling applies to (see <see cref="JobHandlerKeys"/>). Not empty.</summary>
    public required IReadOnlyList<string> HandlerKeys { get; init; }

    /// <summary>Gets the most jobs of <see cref="HandlerKeys"/> one tenant may hold leased. At least one.</summary>
    public required int MaxLeasedPerTenant { get; init; }
}
