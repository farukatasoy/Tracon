namespace Tracon;

/// <summary>A store that keeps the answers to workflow requests in process memory.</summary>
/// <remarks>
/// <para>
/// <strong>Limits:</strong> process lifetime and a single node. A claim taken here is
/// not seen by another process; in production, use <c>Tracon.PostgreSql</c>,
/// <c>Tracon.SqlServer</c> or <c>Tracon.Sqlite</c>.
/// </para>
/// <para>
/// The record dictionary is itself the lock (the <c>InMemoryJobStore</c> pattern: net8.0 has
/// no <c>System.Threading.Lock</c>, and a separate lock object would trigger MA0158), so a
/// claim and a replacement are atomic the way the SQL stores make them atomic with a unique
/// index and a transaction. Records of a deleted
/// source run are not removed: there is no foreign key in memory.
/// </para>
/// </remarks>
internal sealed class InMemoryWorkflowResponseStore : IWorkflowResponseStore
{
    private readonly Dictionary<Guid, WorkflowResponseRecord> _records = [];
    private readonly ITenantContext _tenantContext;

    /// <summary>Creates the store.</summary>
    /// <param name="tenantContext">
    /// The current tenant context. When it is not supplied, the store behaves as single tenant.
    /// </param>
    public InMemoryWorkflowResponseStore(ITenantContext? tenantContext = null)
    {
        _tenantContext = tenantContext ?? FixedTenantContext.Default;
    }

    /// <inheritdoc />
    public ValueTask<WorkflowResponseClaim> AddAsync(WorkflowResponseRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.TenantId, nameof(record));
        cancellationToken.ThrowIfCancellationRequested();

        lock (_records)
        {
            if (record.ClaimScope is { } scope && FindClaim(record.TenantId, scope, record.RequestId) is { } existing)
            {
                return ValueTask.FromResult(new WorkflowResponseClaim { Claimed = false, Existing = existing });
            }

            Insert(record);
        }

        return ValueTask.FromResult(new WorkflowResponseClaim { Claimed = true, Existing = null });
    }

    /// <inheritdoc />
    public ValueTask<WorkflowResponseRecord?> FindClaimAsync(
        string claimScope,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(claimScope);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_records)
        {
            return ValueTask.FromResult(FindClaim(_tenantContext.TenantId, claimScope, requestId));
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> ReplaceAsync(
        Guid abandonedResponseRunId,
        WorkflowResponseRecord replacement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        ArgumentException.ThrowIfNullOrWhiteSpace(replacement.TenantId, nameof(replacement));
        cancellationToken.ThrowIfCancellationRequested();

        lock (_records)
        {
            if (!_records.TryGetValue(abandonedResponseRunId, out var existing) ||
                !string.Equals(existing.TenantId, _tenantContext.TenantId, StringComparison.Ordinal) ||
                existing.Status == WorkflowResponseStatus.Abandoned)
            {
                return ValueTask.FromResult(false);
            }

            _records[abandonedResponseRunId] = existing with { Status = WorkflowResponseStatus.Abandoned };
            Insert(replacement);
        }

        return ValueTask.FromResult(true);
    }

    /// <inheritdoc />
    public ValueTask MarkDeliveredAsync(Guid responseRunId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_records)
        {
            if (_records.TryGetValue(responseRunId, out var existing) &&
                string.Equals(existing.TenantId, _tenantContext.TenantId, StringComparison.Ordinal) &&
                existing.Status == WorkflowResponseStatus.Accepted)
            {
                _records[responseRunId] = existing with { Status = WorkflowResponseStatus.Delivered };
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<WorkflowResponseRecord>> ListBySourceRunAsync(
        Guid sourceRunId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tenantId = _tenantContext.TenantId;

        lock (_records)
        {
            IReadOnlyList<WorkflowResponseRecord> result = _records.Values
                .Where(record => record.SourceRunId == sourceRunId &&
                                 string.Equals(record.TenantId, tenantId, StringComparison.Ordinal))
                .OrderBy(static record => record.RespondedAt)
                .ThenBy(static record => record.ResponseRunId)
                .ToList();

            return ValueTask.FromResult(result);
        }
    }

    /// <inheritdoc />
    public ValueTask<WorkflowResponseRecord?> GetAsync(Guid responseRunId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_records)
        {
            var record = _records.TryGetValue(responseRunId, out var found) &&
                         string.Equals(found.TenantId, _tenantContext.TenantId, StringComparison.Ordinal)
                ? found
                : null;

            return ValueTask.FromResult(record);
        }
    }

    private WorkflowResponseRecord? FindClaim(string? tenantId, string claimScope, string requestId)
        => _records.Values.FirstOrDefault(record =>
            record.Status != WorkflowResponseStatus.Abandoned &&
            string.Equals(record.TenantId, tenantId, StringComparison.Ordinal) &&
            string.Equals(record.ClaimScope, claimScope, StringComparison.Ordinal) &&
            string.Equals(record.RequestId, requestId, StringComparison.Ordinal));

    private void Insert(WorkflowResponseRecord record)
    {
        // The primary key, as in the SQL stores: a run answers one request once.
        if (!_records.TryAdd(record.ResponseRunId, record))
        {
            throw new InvalidOperationException(
                $"An answer is already recorded for run '{record.ResponseRunId}'.");
        }
    }
}
