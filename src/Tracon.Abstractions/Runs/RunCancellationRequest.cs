namespace Tracon;

/// <summary>
/// A cancellation request that an operator wrote onto a run another instance may own,
/// as <see cref="IRunStore.TouchHeartbeatAsync"/> reads it back.
/// </summary>
/// <remarks>
/// The owning instance hands <see cref="RunId"/> and <see cref="TenantId"/> to
/// <see cref="IRunCancellationRegistry.TryCancel"/>. The tenant is the run's own tenant,
/// as the row stores it; it is never the ambient tenant.
/// </remarks>
public sealed record RunCancellationRequest
{
    /// <summary>Gets the id of the run whose cancellation was requested.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Gets the tenant the run belongs to.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets the time the first request was written (UTC).</summary>
    public required DateTimeOffset RequestedAt { get; init; }
}
