using System.Data.Common;

namespace AgentPrism;

/// <summary>Stores single-executor election leases in the SQL database (Phase 42).</summary>
/// <remarks>
/// <para>
/// A lease table is used instead of a session lock (<c>pg_try_advisory_lock</c>/
/// <c>sp_getapplock</c>): it behaves the same way across all three providers
/// (SQLite has no session-lock equivalent) and does not depend on connection
/// pooling. Rationale: <c>docs/42-TEK-YURUTUCU-SECIMI.md</c> section 42.3.
/// </para>
/// <para>There is no tenant column: single-executor election is a deployment-wide concept.</para>
/// </remarks>
internal sealed class SqlSingletonLeaseStore : ISingletonLeaseStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Creates a new SQL single-executor lease store.</summary>
    /// <param name="context">The store context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> <see langword="null"/> ise.</exception>
    public SqlSingletonLeaseStore(SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    [TenantAgnostic(
        "Single-executor election is a deployment-wide concept; the lease belongs to a cluster-wide job, not a tenant.")]
    public async ValueTask<bool> TryAcquireAsync(
        string name,
        string ownerId,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        var now = DateTimeOffset.UtcNow;

        var command = CreateCommand(_sql.AcquireSingletonLease);
        DbHelpers.Add(command, "name", name);
        DbHelpers.Add(command, "owner_id", ownerId);
        Dialect.AddTimestamp(command, "expires_at", now + duration);
        Dialect.AddTimestamp(command, "now", now);

        var result = await DbHelpers.ExecuteScalarAsync(command, cancellationToken).ConfigureAwait(false);

        return result is not null;
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Same rationale as TryAcquireAsync: single-executor election is a deployment-wide concept.")]
    public async ValueTask<bool> RenewAsync(
        string name,
        string ownerId,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        var now = DateTimeOffset.UtcNow;

        var command = CreateCommand(_sql.RenewSingletonLease);
        DbHelpers.Add(command, "name", name);
        DbHelpers.Add(command, "owner_id", ownerId);
        Dialect.AddTimestamp(command, "expires_at", now + duration);
        Dialect.AddTimestamp(command, "now", now);

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Same rationale as TryAcquireAsync: single-executor election is a deployment-wide concept.")]
    public async ValueTask ReleaseAsync(string name, string ownerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        var command = CreateCommand(_sql.ReleaseSingletonLease);
        DbHelpers.Add(command, "name", name);
        DbHelpers.Add(command, "owner_id", ownerId);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);
}
