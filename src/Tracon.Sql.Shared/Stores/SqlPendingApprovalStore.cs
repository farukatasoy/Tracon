using System.Data.Common;
using System.Text.Json;

namespace Tracon;

/// <summary>Stores pending approval requests in the SQL database.</summary>
/// <remarks>
/// The behavior contract is identical to <c>InMemoryPendingApprovalStore</c>
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

        await _context.ExecuteStatementAsync(
            _sql.InsertPendingApproval,
            command =>
            {
                DbHelpers.Add(command, "id", approval.Id);
                DbHelpers.AddTenant(command, approval.TenantId);
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
                Dialect.AddJsonb(
                    command,
                    "presentation",
                    approval.Presentation is null
                        ? null
                        : JsonSerializer.Serialize(approval.Presentation, TraconJsonContext.Default.ToolApprovalPresentation));
            },
            DbHelpers.ExecuteAsync,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<PendingApproval>> ListPendingAsync(CancellationToken cancellationToken = default)
    {
        return await _context.ExecuteStatementAsync(
            _sql.SelectPendingApprovals,
            command =>
            {
                DbHelpers.AddTenant(command, _tenantContext.TenantId);
                DbHelpers.Add(command, "status", (short)ApprovalStatus.Pending);
            },
            (command, token) => DbHelpers.ReadListAsync(command, ReadApproval, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<PendingApproval?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.ExecuteStatementAsync(
            _sql.SelectPendingApproval,
            command =>
            {
                DbHelpers.Add(command, "id", id);
                DbHelpers.AddTenant(command, _tenantContext.TenantId);
            },
            (command, token) => DbHelpers.ReadSingleAsync(command, ReadApproval, token),
            cancellationToken).ConfigureAwait(false);
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

        return await _context.ExecuteStatementAsync(
            _sql.DecidePendingApproval,
            command =>
            {
                DbHelpers.Add(command, "id", id);
                DbHelpers.AddTenant(command, _tenantContext.TenantId);
                DbHelpers.Add(
                    command,
                    "status",
                    (short)(approved ? ApprovalStatus.Approved : ApprovalStatus.Rejected));
                DbHelpers.Add(command, "status_pending", (short)ApprovalStatus.Pending);
                DbHelpers.Add(command, "decided_by", decidedBy);
                Dialect.AddTimestamp(command, "decided_at", decidedAt);
            },
            DbHelpers.ExecuteAsync,
            cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Maintenance work: expiry sweeps the overdue requests of EVERY tenant in one pass, so the query carries no tenant_id by design. The interface documents the same thing; the attribute was missing here and the method was silenced through the coverage list instead, which let the mandatory written justification skip code review.")]
    public async ValueTask<IReadOnlyList<PendingApproval>> ExpireAsync(
        DateTimeOffset olderThan,
        int max,
        CancellationToken cancellationToken = default)
    {
        return await _context.ExecuteStatementAsync(
            _sql.ExpirePendingApprovals,
            command =>
            {
                DbHelpers.Add(command, "status_expired", (short)ApprovalStatus.Expired);
                DbHelpers.Add(command, "status_pending", (short)ApprovalStatus.Pending);
                Dialect.AddTimestamp(command, "older_than", olderThan);
                DbHelpers.Add(command, "max", Math.Max(max, 0));
            },
            (command, token) => DbHelpers.ReadListAsync(command, ReadApproval, token),
            cancellationToken).ConfigureAwait(false);
    }

    // 🚨 New columns are ALWAYS appended at the end:
    // presentation reads by fixed ordinal 12, after the original twelve columns.
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
            Presentation = ReadPresentation(DbHelpers.GetNullableString(reader, 12)),
        };

    private static ToolApprovalPresentation? ReadPresentation(string? json)
        => string.IsNullOrEmpty(json)
            ? null
            : JsonSerializer.Deserialize(json, TraconJsonContext.Default.ToolApprovalPresentation);
}
