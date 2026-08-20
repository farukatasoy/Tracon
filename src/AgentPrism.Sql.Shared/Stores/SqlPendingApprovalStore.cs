using System.Data.Common;

namespace AgentPrism;

/// <summary>Stores pending approval requests in the SQL database.</summary>
/// <remarks>
/// The behavior contract is identical to <see cref="InMemoryPendingApprovalStore"/>
/// and is guarded by the shared contract tests. <see cref="ListPendingAsync"/>,
/// <see cref="GetAsync"/>, and <see cref="DecideAsync"/> are scoped to the
/// caller's tenant (<see cref="ITenantContext"/>) — <see cref="ExpireAsync"/> is
/// a maintenance operation and scans every tenant.
/// </remarks>
internal sealed class SqlPendingApprovalStore : IPendingApprovalStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;
    private readonly ITenantContext _tenantContext;

    /// <summary>Creates a new SQL approval store.</summary>
    /// <param name="context">The store context.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public SqlPendingApprovalStore(SqlStoreContext context, ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _context = context;
        _sql = context.Sql;
        _tenantContext = tenantContext;
    }

    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask CreateAsync(PendingApproval approval, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approval);

        var command = CreateCommand(_sql.InsertPendingApproval);
        DbHelpers.Add(command, "id", approval.Id);
        DbHelpers.Add(command, "tenant_id", approval.TenantId);
        DbHelpers.Add(command, "run_id", approval.RunId);
        DbHelpers.Add(command, "session_id", approval.SessionId);
        DbHelpers.Add(command, "request_id", approval.RequestId);
        DbHelpers.Add(command, "tool_name", approval.ToolName);
        Dialect.AddText(command, "arguments", approval.Arguments);
        DbHelpers.Add(command, "status", (short)approval.Status);
        Dialect.AddText(command, "decided_by", approval.DecidedBy);
        Dialect.AddTimestamp(command, "decided_at", approval.DecidedAt);
        Dialect.AddTimestamp(command, "expires_at", approval.ExpiresAt);
        Dialect.AddTimestamp(command, "created_at", approval.CreatedAt);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<PendingApproval>> ListPendingAsync(CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectPendingApprovals);
        DbHelpers.Add(command, "tenant_id", _tenantContext.TenantId);
        DbHelpers.Add(command, "status", (short)ApprovalStatus.Pending);

        return await DbHelpers.ReadListAsync(command, ReadApproval, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<PendingApproval?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectPendingApproval);
        DbHelpers.Add(command, "id", id);
        DbHelpers.Add(command, "tenant_id", _tenantContext.TenantId);

        return await DbHelpers.ReadSingleAsync(command, ReadApproval, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> DecideAsync(
        Guid id,
        bool approved,
        string decidedBy,
        DateTimeOffset decidedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(decidedBy);

        var command = CreateCommand(_sql.DecidePendingApproval);
        DbHelpers.Add(command, "id", id);
        DbHelpers.Add(command, "tenant_id", _tenantContext.TenantId);
        DbHelpers.Add(
            command,
            "status",
            (short)(approved ? ApprovalStatus.Approved : ApprovalStatus.Rejected));
        DbHelpers.Add(command, "status_pending", (short)ApprovalStatus.Pending);
        DbHelpers.Add(command, "decided_by", decidedBy);
        Dialect.AddTimestamp(command, "decided_at", decidedAt);

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<PendingApproval>> ExpireAsync(
        DateTimeOffset olderThan,
        int max,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.ExpirePendingApprovals);
        DbHelpers.Add(command, "status_expired", (short)ApprovalStatus.Expired);
        DbHelpers.Add(command, "status_pending", (short)ApprovalStatus.Pending);
        Dialect.AddTimestamp(command, "older_than", olderThan);
        DbHelpers.Add(command, "max", Math.Max(max, 0));

        return await DbHelpers.ReadListAsync(command, ReadApproval, cancellationToken).ConfigureAwait(false);
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    private static PendingApproval ReadApproval(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            RunId = reader.GetGuid(2),
            SessionId = reader.GetString(3),
            RequestId = reader.GetString(4),
            ToolName = reader.GetString(5),
            Arguments = DbHelpers.GetNullableString(reader, 6),
            Status = (ApprovalStatus)reader.GetInt16(7),
            DecidedBy = DbHelpers.GetNullableString(reader, 8),
            DecidedAt = DbHelpers.GetNullableTimestamp(reader, 9),
            ExpiresAt = DbHelpers.GetTimestamp(reader, 10),
            CreatedAt = DbHelpers.GetTimestamp(reader, 11),
        };
}
