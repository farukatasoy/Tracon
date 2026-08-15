using System.Collections.Concurrent;

namespace AgentPrism;

/// <summary>A store that keeps pending approval requests in process memory.</summary>
/// <remarks>
/// <strong>Limits:</strong> process lifetime and a single node. In production, use
/// <c>AgentPrism.PostgreSql</c> or <c>AgentPrism.SqlServer</c>/<c>AgentPrism.Sqlite</c>.
/// </remarks>
public sealed class InMemoryPendingApprovalStore : IPendingApprovalStore
{
    private readonly ConcurrentDictionary<Guid, PendingApproval> _approvals = new();
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes a new in-memory approval store.</summary>
    /// <param name="tenantContext">
    /// The current tenant context. If this is not supplied, the store behaves as single tenant.
    /// </param>
    public InMemoryPendingApprovalStore(ITenantContext? tenantContext = null)
    {
        _tenantContext = tenantContext ?? FixedTenantContext.Default;
    }

    /// <inheritdoc />
    public ValueTask CreateAsync(PendingApproval approval, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approval);

        _approvals[approval.Id] = approval;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<PendingApproval>> ListPendingAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;

        IReadOnlyList<PendingApproval> result = _approvals.Values
            .Where(approval => approval.Status == ApprovalStatus.Pending &&
                                string.Equals(approval.TenantId, tenantId, StringComparison.Ordinal))
            .OrderBy(static approval => approval.CreatedAt)
            .ToList();

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public ValueTask<PendingApproval?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;

        var approval = _approvals.TryGetValue(id, out var found) &&
                        string.Equals(found.TenantId, tenantId, StringComparison.Ordinal)
            ? found
            : null;

        return ValueTask.FromResult(approval);
    }

    /// <inheritdoc />
    public ValueTask<bool> DecideAsync(
        Guid id,
        bool approved,
        string decidedBy,
        DateTimeOffset decidedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decidedBy);

        var tenantId = _tenantContext.TenantId;

        if (!_approvals.TryGetValue(id, out var existing) ||
            !string.Equals(existing.TenantId, tenantId, StringComparison.Ordinal) ||
            existing.Status != ApprovalStatus.Pending)
        {
            return ValueTask.FromResult(false);
        }

        var decided = existing with
        {
            Status = approved ? ApprovalStatus.Approved : ApprovalStatus.Rejected,
            DecidedBy = decidedBy,
            DecidedAt = decidedAt,
        };

        var updated = _approvals.TryUpdate(id, decided, existing);

        return ValueTask.FromResult(updated);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<PendingApproval>> ExpireAsync(
        DateTimeOffset olderThan,
        int max,
        CancellationToken cancellationToken = default)
    {
        var expired = new List<PendingApproval>();

        foreach (var approval in _approvals.Values.OrderBy(static a => a.ExpiresAt))
        {
            if (expired.Count >= max)
            {
                break;
            }

            if (approval.Status != ApprovalStatus.Pending || approval.ExpiresAt >= olderThan)
            {
                continue;
            }

            var closed = approval with { Status = ApprovalStatus.Expired };

            if (_approvals.TryUpdate(approval.Id, closed, approval))
            {
                expired.Add(closed);
            }
        }

        IReadOnlyList<PendingApproval> result = expired;

        return ValueTask.FromResult(result);
    }
}
