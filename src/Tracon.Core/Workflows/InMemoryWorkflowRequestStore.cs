namespace Tracon;

/// <summary>A store that keeps the inbox of workflow requests in process memory.</summary>
/// <remarks>
/// <para>
/// <strong>Limits:</strong> process lifetime and a single node. A row written here is not seen by
/// another process, and no other node's expiry closes it; in production, use
/// <c>Tracon.PostgreSql</c>, <c>Tracon.SqlServer</c> or <c>Tracon.Sqlite</c>.
/// </para>
/// <para>
/// The row dictionary is itself the lock (the <c>InMemoryJobStore</c> pattern: net8.0 has no
/// <c>System.Threading.Lock</c>, and a separate lock object would trigger MA0158), so an answer
/// and an expiry that meet on one row are atomic the way the SQL stores make them atomic with one
/// statement. Rows of a deleted run are not removed: there is no foreign key in memory.
/// </para>
/// </remarks>
internal sealed class InMemoryWorkflowRequestStore : IWorkflowRequestStore
{
    private readonly Dictionary<(Guid RunId, string RequestId), WorkflowRequestRecord> _records = [];
    private readonly ITenantContext _tenantContext;

    /// <summary>Creates the store.</summary>
    /// <param name="tenantContext">
    /// The current tenant context. When it is not supplied, the store behaves as single tenant.
    /// </param>
    public InMemoryWorkflowRequestStore(ITenantContext? tenantContext = null)
    {
        _tenantContext = tenantContext ?? FixedTenantContext.Default;
    }

    /// <inheritdoc />
    public ValueTask AddAsync(IReadOnlyList<WorkflowRequestRecord> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var record in records)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(record.TenantId, nameof(records));
            ArgumentException.ThrowIfNullOrWhiteSpace(record.RequestId, nameof(records));

            if (record.Status != WorkflowRequestStatus.Open)
            {
                throw new ArgumentException("A row is written open.", nameof(records));
            }
        }

        lock (_records)
        {
            foreach (var record in records)
            {
                // The primary key, as in the SQL stores: the same run and request is written once.
                _records.TryAdd((record.RunId, record.RequestId), record);
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<WorkflowRequestRecord>> ListAsync(
        WorkflowRequestQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var tenantId = _tenantContext.TenantId;
        var skip = Math.Max(query.Skip, 0);
        var take = Math.Clamp(query.Take, 1, WorkflowRequestQuery.MaxTake);

        lock (_records)
        {
            // One row per logical request: the newest open row of a session's request id.
            IReadOnlyList<WorkflowRequestRecord> page = _records.Values
                .Where(record => record.Status == WorkflowRequestStatus.Open &&
                                 string.Equals(record.TenantId, tenantId, StringComparison.Ordinal))
                .GroupBy(static record => (record.SessionId, record.RequestId))
                .Select(static group => group
                    .OrderByDescending(static record => record.RequestedAt)
                    .ThenByDescending(static record => record.RunId)
                    .First())
                .Where(record => query.WorkflowName is null ||
                                 string.Equals(record.WorkflowName, query.WorkflowName, StringComparison.Ordinal))
                .Where(record => query.State switch
                {
                    WorkflowRequestState.Pending => record.AnsweredAt is null,
                    WorkflowRequestState.Answered => record.AnsweredAt is not null,
                    _ => true,
                })
                .OrderBy(static record => record.RequestedAt)
                .ThenBy(static record => record.RunId)
                .ThenBy(static record => record.RequestId, StringComparer.Ordinal)
                .Skip(skip)
                .Take(take)
                .ToList();

            return ValueTask.FromResult(page);
        }
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<WorkflowRequestRecord>> ListByRunAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tenantId = _tenantContext.TenantId;

        lock (_records)
        {
            IReadOnlyList<WorkflowRequestRecord> rows = _records.Values
                .Where(record => record.RunId == runId &&
                                 string.Equals(record.TenantId, tenantId, StringComparison.Ordinal))
                .OrderBy(static record => record.EventSequence)
                .ThenBy(static record => record.RequestId, StringComparer.Ordinal)
                .ToList();

            return ValueTask.FromResult(rows);
        }
    }

    /// <inheritdoc />
    public ValueTask<int> MarkAnsweredAsync(
        string sessionId,
        string requestId,
        Guid responseRunId,
        DateTimeOffset answeredAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        cancellationToken.ThrowIfCancellationRequested();

        var tenantId = _tenantContext.TenantId;
        var changed = 0;

        lock (_records)
        {
            foreach (var (key, record) in _records.ToList())
            {
                if (record.Status != WorkflowRequestStatus.Open ||
                    !string.Equals(record.TenantId, tenantId, StringComparison.Ordinal) ||
                    !string.Equals(record.SessionId, sessionId, StringComparison.Ordinal) ||
                    !string.Equals(record.RequestId, requestId, StringComparison.Ordinal))
                {
                    continue;
                }

                _records[key] = record with
                {
                    AnsweredAt = record.AnsweredAt ?? answeredAt,
                    LastResponseRunId = responseRunId,
                };
                changed++;
            }
        }

        return ValueTask.FromResult(changed);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<WorkflowRequestRecord>> ExpireAsync(
        DateTimeOffset now,
        int max,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var closed = new List<WorkflowRequestRecord>();

        lock (_records)
        {
            var due = _records
                .Where(pair => pair.Value.Status == WorkflowRequestStatus.Open &&
                               pair.Value.ExpiresAt is { } expiresAt &&
                               expiresAt <= now)
                .OrderBy(static pair => pair.Value.ExpiresAt)
                .ThenBy(static pair => pair.Value.RunId)
                .Take(Math.Max(max, 0))
                .Select(static pair => pair.Key)
                .ToList();

            foreach (var key in due)
            {
                var record = _records[key] with { Status = WorkflowRequestStatus.Closed, ClosedAt = now };
                _records[key] = record;
                closed.Add(record);
            }
        }

        return ValueTask.FromResult<IReadOnlyList<WorkflowRequestRecord>>(closed);
    }
}
