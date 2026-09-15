namespace Tracon;

/// <summary>The audit trail log.</summary>
/// <remarks>
/// <para>
/// Writing happens in the <strong>store decorators</strong>, not in the endpoint layer:
/// writing in the endpoint layer would miss a change made to the same store from another
/// code path. The decorator is the single gate.
/// </para>
/// <para>
/// <strong>A write failure does not stop the operation — with six named exceptions.</strong>
/// For every audit write but those six, a failed <see cref="WriteAsync"/> is logged and
/// the operation continues: observability does not break behaviour. The record can
/// therefore be missing, which is what an audit trail proves and does not prove.
/// </para>
/// <para>
/// The six FAIL-CLOSED operations write their entry BEFORE the change and do not apply
/// the change if the write fails: an approval decision; granting or revoking a skill
/// script; running a skill script; saving or deleting an inbound trigger; an automatic
/// canary rollback; and a data subject erasure. Each of these is irreversible or
/// security-bearing enough that a missing record is worse than a refused operation, so
/// a failed write surfaces to the caller as an error instead of a warning in a log.
/// </para>
/// <para>
/// That split is a <strong>published guarantee</strong>, not an implementation detail:
/// an implementation of this interface that swallows its own write errors turns six
/// fail-closed operations into best-effort ones without anything else changing. Throw
/// on a write that did not happen. Both paths count a failure on the
/// <c>tracon.audit.write_failures</c> counter, tagged <c>swallowed</c> or
/// <c>refused</c>, so the difference stays visible in production.
/// </para>
/// <para>There is a read endpoint only; there is no delete or edit endpoint, and there will not be one.</para>
/// <para>
/// <strong>DI lifetime.</strong> Registered as a <em>singleton</em> with <c>TryAdd</c>;
/// a consumer's own registration wins. An implementation must be safe under concurrent
/// calls and must not capture or depend on a scoped service.
/// </para>
/// <para>
/// <strong>Tenant behavior — EXPECTED tenant, with an AMBIENT fallback.</strong>
/// <see cref="WriteAsync"/> is scoped by the entry's own <c>AuditEntry.TenantId</c>
/// field — an explicit, EXPECTED tenant that is never inferred. <see cref="QueryAsync"/>
/// and <see cref="VerifyChainAsync"/> accept an explicit <c>TenantId</c> override
/// (<see cref="AuditQuery.TenantId"/>, <see cref="AuditChainQuery.TenantId"/>) and fall
/// back to the AMBIENT <see cref="ITenantContext.TenantId"/> ONLY when that override is
/// <see langword="null"/> or empty. A <see langword="null"/> <c>TenantId</c> is a
/// <strong>contract, not a convenience</strong>: it MUST resolve to the single ambient
/// tenant, never to "every tenant". An implementation that treats a missing filter as "no
/// filter" returns every tenant's records to whoever leaves the field unset — this is
/// exactly the shape <c>AuditLogContract</c>'s ambient-fallback scenarios (BL-046) guard
/// against; the built-in <c>InMemoryAuditLog</c> and <c>SqlAuditLog</c> both resolve the
/// fallback through an injected <see cref="ITenantContext"/>, the same pattern
/// <c>IRunStore</c>'s AMBIENT methods use.
/// </para>
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
