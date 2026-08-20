namespace AgentPrism;

/// <summary>The audit trail log.</summary>
/// <remarks>
/// <para>
/// Writing happens in the <strong>store decorators</strong>, not in the endpoint layer:
/// writing in the endpoint layer would miss a change made to the same store from another
/// code path. The decorator is the single gate.
/// </para>
/// <para>
/// <strong>A write failure does not stop the operation.</strong> When
/// <see cref="WriteAsync"/> fails the caller logs it and the operation continues — the
/// same rule: observability does not break behaviour.
/// </para>
/// <para>There is a read endpoint only; there is no delete or edit endpoint, and there will not be one.</para>
/// </remarks>
public interface IAuditLog
{
    /// <summary>Writes an audit record.</summary>
    /// <param name="entry">The record to write.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Reads the records through a filter.</summary>
    /// <param name="query">The filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The records, newest first.</returns>
    ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default);

    /// <summary>Walks a tenant's hash chain and reports whether it is intact.</summary>
    /// <param name="query">The scope: tenant and, optionally, a date range.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The verification result.</returns>
    /// <remarks>
    /// A date range narrows which entries are walked; it does not weaken the check
    /// within that range. Because the entry immediately before the range's start is
    /// not read, a break at the range's own boundary cannot be judged and is not
    /// reported — an unbounded query is the only way to check a tenant's whole history.
    /// </remarks>
    ValueTask<AuditChainVerification> VerifyChainAsync(
        AuditChainQuery query,
        CancellationToken cancellationToken = default);
}
