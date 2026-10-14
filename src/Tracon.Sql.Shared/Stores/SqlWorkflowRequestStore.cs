using System.Data.Common;

namespace Tracon;

/// <summary>Stores the inbox of workflow requests in the SQL database.</summary>
/// <remarks>
/// <para>
/// The behavior contract is identical to <c>InMemoryWorkflowRequestStore</c> and is guarded by
/// the shared contract tests. Reads and updates are scoped to the caller's tenant
/// (<see cref="ITenantContext"/>); a write takes the tenant from the record, and
/// <see cref="ExpireAsync"/> is maintenance work that scans every tenant.
/// </para>
/// <para>
/// <strong>The expiry is one statement.</strong> Selecting the due rows and closing them happen in
/// one <c>UPDATE</c> that returns the closed rows, whose <c>WHERE</c> repeats <c>status = open</c>:
/// of two statements that pick the same row, the second finds it closed and returns nothing.
/// </para>
/// </remarks>
internal sealed class SqlWorkflowRequestStore : IWorkflowRequestStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;
    private readonly ITenantContext _tenantContext;

    /// <summary>Creates the store.</summary>
    /// <param name="context">The store context.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public SqlWorkflowRequestStore(SqlStoreContext context, ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _context = context;
        _sql = context.Sql;
        _tenantContext = tenantContext;
    }

    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask AddAsync(
        IReadOnlyList<WorkflowRequestRecord> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        foreach (var record in records)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(record.TenantId, nameof(records));
            ArgumentException.ThrowIfNullOrWhiteSpace(record.RequestId, nameof(records));

            if (record.Status != WorkflowRequestStatus.Open)
            {
                throw new ArgumentException("A row is written open.", nameof(records));
            }
        }

        foreach (var record in records)
        {
            var command = _context.CreateCommand(_sql.InsertWorkflowRequest);
            Bind(command, record);

            try
            {
                await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
            }
            catch (DbException ex) when (Dialect.IsUniqueViolation(ex))
            {
                // The same run and request was written already: writing a run twice is harmless.
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<WorkflowRequestRecord>> ListAsync(
        WorkflowRequestQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = _context.CreateCommand(_sql.SelectWorkflowRequests);
        DbHelpers.AddTenant(command, _tenantContext.TenantId);
        DbHelpers.Add(command, "status_open", (short)WorkflowRequestStatus.Open);
        Dialect.AddText(command, "workflow_name", query.WorkflowName);
        DbHelpers.Add(command, "state", (short)query.State);
        DbHelpers.Add(command, "skip", Math.Max(query.Skip, 0));
        DbHelpers.Add(command, "take", Math.Clamp(query.Take, 1, WorkflowRequestQuery.MaxTake));

        return await DbHelpers.ReadListAsync(command, Read, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<WorkflowRequestRecord>> ListByRunAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var command = _context.CreateCommand(_sql.SelectWorkflowRequestsByRun);
        DbHelpers.AddTenant(command, _tenantContext.TenantId);
        DbHelpers.Add(command, "run_id", runId);

        return await DbHelpers.ReadListAsync(command, Read, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<int> MarkAnsweredAsync(
        string sessionId,
        string requestId,
        Guid responseRunId,
        DateTimeOffset answeredAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

        var command = _context.CreateCommand(_sql.MarkWorkflowRequestsAnswered);
        DbHelpers.AddTenant(command, _tenantContext.TenantId);
        DbHelpers.Add(command, "session_id", sessionId);
        DbHelpers.Add(command, "request_id", requestId);
        Dialect.AddTimestamp(command, "answered_at", answeredAt);
        Dialect.AddUuid(command, "last_response_run_id", responseRunId);
        DbHelpers.Add(command, "status_open", (short)WorkflowRequestStatus.Open);

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Maintenance work: the expiry closes the overdue requests of EVERY tenant in one pass, so the statement carries no tenant_id by design. The interface documents the same thing (the same rationale as IPendingApprovalStore.ExpireAsync).")]
    public async ValueTask<IReadOnlyList<WorkflowRequestRecord>> ExpireAsync(
        DateTimeOffset now,
        int max,
        CancellationToken cancellationToken = default)
    {
        var command = _context.CreateCommand(_sql.ExpireWorkflowRequests);
        DbHelpers.Add(command, "status_open", (short)WorkflowRequestStatus.Open);
        DbHelpers.Add(command, "status_closed", (short)WorkflowRequestStatus.Closed);
        Dialect.AddTimestamp(command, "now", now);
        DbHelpers.Add(command, "max", Math.Max(max, 0));

        return await DbHelpers.ReadListAsync(command, Read, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Maintenance work: the recovery of a run left waiting after its rows were closed looks at the runs of EVERY tenant in one pass, so the statement carries no tenant_id by design (the same rationale as ExpireAsync).")]
    public async ValueTask<IReadOnlyList<WorkflowRequestRecord>> ListStrandedAsync(
        int max,
        CancellationToken cancellationToken = default)
    {
        var command = _context.CreateCommand(_sql.SelectStrandedWorkflowRequests);
        DbHelpers.Add(command, "status_open", (short)WorkflowRequestStatus.Open);
        DbHelpers.Add(command, "status_closed", (short)WorkflowRequestStatus.Closed);
        DbHelpers.Add(command, "status_awaiting", (short)RunStatus.AwaitingInput);
        DbHelpers.Add(command, "max", Math.Max(max, 0));

        return await DbHelpers.ReadListAsync(command, Read, cancellationToken).ConfigureAwait(false);
    }

    private void Bind(DbCommand command, WorkflowRequestRecord record)
    {
        DbHelpers.Add(command, "run_id", record.RunId);
        DbHelpers.Add(command, "request_id", record.RequestId);
        DbHelpers.AddTenant(command, record.TenantId!);
        DbHelpers.Add(command, "session_id", record.SessionId);
        DbHelpers.Add(command, "workflow_name", record.WorkflowName);
        DbHelpers.Add(command, "port_id", record.PortId);
        DbHelpers.Add(command, "event_seq", record.EventSequence);
        Dialect.AddTimestamp(command, "requested_at", record.RequestedAt);
        Dialect.AddTimestamp(command, "expires_at", record.ExpiresAt);
        DbHelpers.Add(command, "status", (short)record.Status);
        Dialect.AddTimestamp(command, "answered_at", record.AnsweredAt);
        Dialect.AddUuid(command, "last_response_run_id", record.LastResponseRunId);
        Dialect.AddTimestamp(command, "closed_at", record.ClosedAt);
    }

    // The column order of SqlQueriesBase.WorkflowRequestColumns.
    private static WorkflowRequestRecord Read(DbDataReader reader)
        => new()
        {
            RunId = reader.GetGuid(0),
            RequestId = reader.GetString(1),
            TenantId = reader.GetString(2),
            SessionId = reader.GetString(3),
            WorkflowName = reader.GetString(4),
            PortId = reader.GetString(5),
            EventSequence = reader.GetInt64(6),
            RequestedAt = DbHelpers.GetTimestamp(reader, 7),
            ExpiresAt = DbHelpers.GetNullableTimestamp(reader, 8),
            Status = (WorkflowRequestStatus)reader.GetInt16(9),
            AnsweredAt = DbHelpers.GetNullableTimestamp(reader, 10),
            LastResponseRunId = DbHelpers.GetNullableGuid(reader, 11),
            ClosedAt = DbHelpers.GetNullableTimestamp(reader, 12),
        };
}
