namespace AgentPrism;

/// <summary>The store for pending approval requests.</summary>
/// <remarks>
/// <see cref="ListPendingAsync"/>, <see cref="GetAsync"/> and <see cref="DecideAsync"/>
/// are limited to the tenant of the caller (<c>ITenantContext</c>): the operator of one
/// tenant can neither see nor decide the approvals of another tenant.
/// <see cref="ExpireAsync"/> is a maintenance operation and scans every tenant — the
/// same rationale as <c>IRunStore.ClaimOrphanedRunsAsync</c>.
/// </remarks>
public interface IPendingApprovalStore
{
    /// <summary>Creates a new pending approval request.</summary>
    /// <param name="approval">
    /// The request to save. <see cref="PendingApproval.Status"/> must be <see
    /// cref="ApprovalStatus.Pending"/>.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask CreateAsync(PendingApproval approval, CancellationToken cancellationToken = default);

    /// <summary>Lists the pending requests of the caller's tenant, ordered from oldest to newest.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask<IReadOnlyList<PendingApproval>> ListPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns a single pending approval request.</summary>
    /// <param name="id">The request id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The request, or <see langword="null"/> when it is not found in the caller's tenant.</returns>
    ValueTask<PendingApproval?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Writes a decision on a pending request.</summary>
    /// <param name="id">The request id.</param>
    /// <param name="approved">Whether the request is approved.</param>
    /// <param name="decidedBy">The actor that made the decision.</param>
    /// <param name="decidedAt">The time of the decision.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the decision is written; <see langword="false"/> when
    /// the request is not found in the caller's tenant or is no longer in the
    /// <see cref="ApprovalStatus.Pending"/> state (a second decision is rejected silently).
    /// </returns>
    ValueTask<bool> DecideAsync(
        Guid id,
        bool approved,
        string decidedBy,
        DateTimeOffset decidedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the requests that expired before <paramref name="olderThan"/> and are still
    /// in the <see cref="ApprovalStatus.Pending"/> state, marking them
    /// <see cref="ApprovalStatus.Expired"/>.
    /// </summary>
    /// <param name="olderThan">Requests that expired before this time are closed (UTC).</param>
    /// <param name="max">The upper number of requests closed in this round.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The records of the closed requests.</returns>
    /// <remarks>
    /// <c>[TenantAgnostic]</c>: this is maintenance work and scans the expired requests
    /// of every tenant; filtering by the ambient tenant would leave the requests of the
    /// other tenants <see cref="ApprovalStatus.Pending"/> forever.
    /// </remarks>
    ValueTask<IReadOnlyList<PendingApproval>> ExpireAsync(
        DateTimeOffset olderThan,
        int max,
        CancellationToken cancellationToken = default);
}
