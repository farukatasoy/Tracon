using System.Data.Common;

namespace Tracon;

/// <summary>Stores the answers to workflow requests in the SQL database.</summary>
/// <remarks>
/// <para>
/// The behavior contract is identical to <c>InMemoryWorkflowResponseStore</c> and is guarded
/// by the shared contract tests. Reads and updates are scoped to the caller's tenant
/// (<see cref="ITenantContext"/>); a write takes the tenant from the record.
/// </para>
/// <para>
/// <strong>The claim is the index's job.</strong> <c>workflow_responses_claim_idx</c> is a
/// partial unique index over <c>(tenant_id, claim_scope, request_id)</c> of the rows that are
/// not abandoned, so of two concurrent inserts with the same claim the database writes one and
/// fails the other with a unique violation. The loser reads the winner, the same way
/// <c>SqlIdempotencyStore</c> reads the key it lost to.
/// </para>
/// </remarks>
internal sealed class SqlWorkflowResponseStore : IWorkflowResponseStore
{
    /// <summary>
    /// How many times an insert is tried when it loses a claim and the winner is gone before it
    /// can be read (a replacement freed and retook the claim in between).
    /// </summary>
    private const int ClaimAttempts = 3;

    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;
    private readonly ITenantContext _tenantContext;

    /// <summary>Creates the store.</summary>
    /// <param name="context">The store context.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public SqlWorkflowResponseStore(SqlStoreContext context, ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _context = context;
        _sql = context.Sql;
        _tenantContext = tenantContext;
    }

    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask<WorkflowResponseClaim> AddAsync(
        WorkflowResponseRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.TenantId, nameof(record));

        for (var attempt = 1; ; attempt++)
        {
            var command = _context.CreateCommand(_sql.InsertWorkflowResponse);
            Bind(command, record);

            try
            {
                await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);

                return new WorkflowResponseClaim { Claimed = true, Existing = null };
            }
            catch (DbException ex) when (record.ClaimScope is not null && Dialect.IsUniqueViolation(ex))
            {
                if (await FindClaimAsync(record.TenantId, record.ClaimScope, record.RequestId, cancellationToken)
                        .ConfigureAwait(false) is { } existing)
                {
                    return new WorkflowResponseClaim { Claimed = false, Existing = existing };
                }

                if (attempt == ClaimAttempts)
                {
                    throw new TraconException(
                        $"The answer to request '{record.RequestId}' conflicted with another answer, but that answer " +
                        "could not be read.",
                        ex);
                }
            }
        }
    }

    /// <inheritdoc />
    public ValueTask<WorkflowResponseRecord?> FindClaimAsync(
        string claimScope,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(claimScope);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

        return FindClaimAsync(_tenantContext.TenantId, claimScope, requestId, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<bool> ReplaceAsync(
        Guid abandonedResponseRunId,
        WorkflowResponseRecord replacement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        ArgumentException.ThrowIfNullOrWhiteSpace(replacement.TenantId, nameof(replacement));

        var tenantId = _tenantContext.TenantId;

        // One transaction: the abandon frees the claim and the insert takes it, and no reader
        // ever sees the request unclaimed. The abandon's WHERE is the compare-and-swap - of two
        // concurrent replacements the second waits on the row lock and then matches nothing.
        return await _context.ExecuteInTransactionAsync(
            async (connection, transaction) =>
            {
                var abandon = _context.CreateCommand(_sql.AbandonWorkflowResponse, connection, transaction);
                DbHelpers.Add(abandon, "response_run_id", abandonedResponseRunId);
                DbHelpers.AddTenant(abandon, tenantId);
                DbHelpers.Add(abandon, "status_abandoned", (short)WorkflowResponseStatus.Abandoned);

                if (await DbHelpers.ExecuteAsync(abandon, cancellationToken).ConfigureAwait(false) == 0)
                {
                    return false;
                }

                var insert = _context.CreateCommand(_sql.InsertWorkflowResponse, connection, transaction);
                Bind(insert, replacement);

                await DbHelpers.ExecuteAsync(insert, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask MarkDeliveredAsync(Guid responseRunId, CancellationToken cancellationToken = default)
    {
        var command = _context.CreateCommand(_sql.MarkWorkflowResponseDelivered);
        DbHelpers.Add(command, "response_run_id", responseRunId);
        DbHelpers.AddTenant(command, _tenantContext.TenantId);
        DbHelpers.Add(command, "status_delivered", (short)WorkflowResponseStatus.Delivered);
        DbHelpers.Add(command, "status_accepted", (short)WorkflowResponseStatus.Accepted);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<WorkflowResponseRecord>> ListBySourceRunAsync(
        Guid sourceRunId,
        CancellationToken cancellationToken = default)
    {
        var command = _context.CreateCommand(_sql.SelectWorkflowResponsesBySource);
        DbHelpers.AddTenant(command, _tenantContext.TenantId);
        DbHelpers.Add(command, "source_run_id", sourceRunId);

        return await DbHelpers.ReadListAsync(command, Read, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<WorkflowResponseRecord?> GetAsync(Guid responseRunId, CancellationToken cancellationToken = default)
    {
        var command = _context.CreateCommand(_sql.SelectWorkflowResponse);
        DbHelpers.Add(command, "response_run_id", responseRunId);
        DbHelpers.AddTenant(command, _tenantContext.TenantId);

        return await DbHelpers.ReadSingleAsync(command, Read, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<WorkflowResponseRecord?> FindClaimAsync(
        string tenantId,
        string claimScope,
        string requestId,
        CancellationToken cancellationToken)
    {
        var command = _context.CreateCommand(_sql.SelectWorkflowResponseClaim);
        DbHelpers.AddTenant(command, tenantId);
        DbHelpers.Add(command, "claim_scope", claimScope);
        DbHelpers.Add(command, "request_id", requestId);
        DbHelpers.Add(command, "status_abandoned", (short)WorkflowResponseStatus.Abandoned);

        return await DbHelpers.ReadSingleAsync(command, Read, cancellationToken).ConfigureAwait(false);
    }

    private void Bind(DbCommand command, WorkflowResponseRecord record)
    {
        DbHelpers.Add(command, "response_run_id", record.ResponseRunId);
        DbHelpers.AddTenant(command, record.TenantId!);
        DbHelpers.Add(command, "source_run_id", record.SourceRunId);
        DbHelpers.Add(command, "session_id", record.SessionId);
        DbHelpers.Add(command, "request_id", record.RequestId);
        Dialect.AddText(command, "claim_scope", record.ClaimScope);
        DbHelpers.Add(command, "answer_hash", record.AnswerHash);
        DbHelpers.Add(command, "status", (short)record.Status);
        Dialect.AddText(command, "responded_by", record.RespondedBy);
        Dialect.AddTimestamp(command, "responded_at", record.RespondedAt);
    }

    // The column order of SqlQueriesBase.WorkflowResponseColumns.
    private static WorkflowResponseRecord Read(DbDataReader reader)
        => new()
        {
            ResponseRunId = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            SourceRunId = reader.GetGuid(2),
            SessionId = reader.GetString(3),
            RequestId = reader.GetString(4),
            ClaimScope = DbHelpers.GetNullableString(reader, 5),
            AnswerHash = reader.GetString(6),
            Status = (WorkflowResponseStatus)reader.GetInt16(7),
            RespondedBy = DbHelpers.GetNullableString(reader, 8),
            RespondedAt = DbHelpers.GetTimestamp(reader, 9),
        };
}
