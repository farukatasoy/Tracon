using System.Data.Common;

namespace Tracon;

/// <summary>Persists retention policies and run history.</summary>
/// <remarks>
/// The behavior contract is identical to <c>InMemoryRetentionPolicyStore</c>.
/// For the data plane (the actual deletion), see <see cref="SqlRetentionStore"/>.
/// </remarks>
internal sealed class SqlRetentionPolicyStore : IRetentionPolicyStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Creates a new retention policy store.</summary>
    /// <param name="context">The store context.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public SqlRetentionPolicyStore(SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    /// <summary>The gateway for provider-specific behavior.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<RetentionPolicy>> ListPoliciesAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        return await _context.ExecuteStatementAsync(
            _sql.SelectRetentionPolicies,
            command =>
            {
                DbHelpers.AddPolicyTenant(command, tenantId);
            },
            (command, token) => DbHelpers.ReadListAsync(command, ReadPolicy, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<RetentionPolicy?> GetPolicyAsync(
        string tenantId,
        string target,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        var found = await _context.ExecuteStatementAsync(
            _sql.SelectRetentionPolicy,
            command =>
            {
                DbHelpers.AddPolicyTenant(command, tenantId);
                DbHelpers.Add(command, "target", target);
            },
            (command, token) => DbHelpers.ReadSingleAsync(command, ReadPolicy, token),
            cancellationToken).ConfigureAwait(false);

        if (found is not null || string.Equals(tenantId, RetentionPolicyTenant.AllTenants, StringComparison.Ordinal))
        {
            return found;
        }

        // Falls back to the "*" global default when there is no tenant-specific record.

        return await _context.ExecuteStatementAsync(
            _sql.SelectRetentionPolicy,
            fallback =>
            {
                DbHelpers.AddPolicyTenant(fallback, RetentionPolicyTenant.AllTenants);
                DbHelpers.Add(fallback, "target", target);
            },
            (fallback, token) => DbHelpers.ReadSingleAsync(fallback, ReadPolicy, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<RetentionPolicy> SavePolicyAsync(
        RetentionPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var saved = await _context.ExecuteUpsertAsync(
            _sql.UpsertRetentionPolicy,
            command =>
            {
                DbHelpers.Add(command, "id", policy.Id);
                DbHelpers.AddPolicyTenant(command, policy.TenantId);
                DbHelpers.Add(command, "target", policy.Target);
                Dialect.AddInt32(command, "max_age_days", policy.MaxAgeDays);
                Dialect.AddInt64(command, "max_rows", policy.MaxRows);
                DbHelpers.Add(command, "archive", policy.Archive);
                DbHelpers.Add(command, "enabled", policy.Enabled);
                Dialect.AddTimestamp(command, "created_at", policy.CreatedAt);
                Dialect.AddTimestamp(command, "updated_at", policy.UpdatedAt);
            },
            (command, token) => DbHelpers.ReadSingleAsync(command, ReadPolicy, token),
            cancellationToken).ConfigureAwait(false);

        return saved ?? policy;
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeletePolicyAsync(
        string tenantId,
        string target,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        return await _context.ExecuteStatementAsync(
            _sql.DeleteRetentionPolicy,
            command =>
            {
                DbHelpers.AddPolicyTenant(command, tenantId);
                DbHelpers.Add(command, "target", target);
            },
            DbHelpers.ExecuteAsync,
            cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    public async ValueTask<RetentionRun> CreateRunAsync(RetentionRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);

        await _context.ExecuteStatementAsync(
            _sql.InsertRetentionRun,
            command =>
            {
                DbHelpers.Add(command, "id", run.Id);
                DbHelpers.AddTenant(command, run.TenantId);
                DbHelpers.Add(command, "target", run.Target);
                Dialect.AddTimestamp(command, "started_at", run.StartedAt);
            },
            DbHelpers.ExecuteAsync,
            cancellationToken).ConfigureAwait(false);

        return run;
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Progress is written with the run id generated by the executor that OPENED the run itself; there is no separate tenant intent in the call.")]
    public async ValueTask AppendRunProgressAsync(
        Guid runId,
        long deletedDelta,
        long archivedDelta,
        CancellationToken cancellationToken = default)
    {
        await _context.ExecuteStatementAsync(
            _sql.UpdateRetentionRunProgress,
            command =>
            {
                DbHelpers.Add(command, "id", runId);
                DbHelpers.Add(command, "deleted_delta", deletedDelta);
                DbHelpers.Add(command, "archived_delta", archivedDelta);
            },
            DbHelpers.ExecuteAsync,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Same rationale as AppendRunProgressAsync: the run id comes from CreateRunAsync.")]
    public async ValueTask CompleteRunAsync(
        Guid runId,
        DateTimeOffset completedAt,
        string? errorMessage,
        CancellationToken cancellationToken = default)
    {
        await _context.ExecuteStatementAsync(
            _sql.CompleteRetentionRun,
            command =>
            {
                DbHelpers.Add(command, "id", runId);
                Dialect.AddTimestamp(command, "completed_at", completedAt);
                Dialect.AddText(command, "error", errorMessage);
            },
            DbHelpers.ExecuteAsync,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<RetentionRun>> ListRunsAsync(
        string tenantId,
        string? target,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        return await _context.ExecuteStatementAsync(
            _sql.SelectRetentionRuns,
            command =>
            {
                DbHelpers.AddTenant(command, tenantId);
                Dialect.AddText(command, "target", target);
                DbHelpers.Add(command, "skip", Math.Max(0, skip));
                DbHelpers.Add(command, "take", Math.Max(1, take));
            },
            (command, token) => DbHelpers.ReadListAsync(command, ReadRun, token),
            cancellationToken).ConfigureAwait(false);
    }

    private static RetentionPolicy ReadPolicy(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            Target = reader.GetString(2),
            MaxAgeDays = DbHelpers.GetNullableInt32(reader, 3),
            MaxRows = DbHelpers.GetNullableInt64(reader, 4),
            Archive = reader.GetBoolean(5),
            Enabled = reader.GetBoolean(6),
            CreatedAt = DbHelpers.GetTimestamp(reader, 7),
            UpdatedAt = DbHelpers.GetTimestamp(reader, 8),
        };

    private static RetentionRun ReadRun(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            Target = reader.GetString(2),
            DeletedRows = reader.GetInt64(3),
            ArchivedRows = reader.GetInt64(4),
            StartedAt = DbHelpers.GetTimestamp(reader, 5),
            CompletedAt = DbHelpers.GetNullableTimestamp(reader, 6),
            Error = DbHelpers.GetNullableString(reader, 7),
        };
}
