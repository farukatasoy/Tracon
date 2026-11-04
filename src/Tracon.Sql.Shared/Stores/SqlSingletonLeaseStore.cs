using System.Data.Common;

namespace Tracon;

/// <summary>Stores single-executor election leases in the SQL database.</summary>
/// <remarks>
/// <para>
/// A lease table is used instead of a session lock (<c>pg_try_advisory_lock</c>/
/// <c>sp_getapplock</c>): it behaves the same way across all three providers
/// (SQLite has no session-lock equivalent) and does not depend on connection
/// pooling.
/// </para>
/// <para>There is no tenant column: single-executor election is a deployment-wide concept.</para>
/// <para>
/// On PostgreSQL and SQL Server the time a lease is stamped and compared with is the database server's, read inside
/// the statement: every node that shares the database decides from one clock, so a node whose own clock is wrong cannot
/// take a lease that is live or keep one that is over. SQLite is used by one process and keeps the application clock.
/// </para>
/// </remarks>
internal sealed class SqlSingletonLeaseStore : ISingletonLeaseStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a new SQL single-executor lease store.</summary>
    /// <param name="context">The store context.</param>
    /// <param name="timeProvider">
    /// The clock a lease is stamped and compared with on a provider whose statements take the time as a parameter
    /// (SQLite); PostgreSQL and SQL Server read the database server's clock and ignore it. Defaults to
    /// <see cref="TimeProvider.System"/> when not given.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public SqlSingletonLeaseStore(SqlStoreContext context, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
        _timeProvider = timeProvider ?? TimeProvider.System;
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

        var result = await _context.ExecuteUpsertAsync(
            _sql.AcquireSingletonLease,
            command =>
            {
                DbHelpers.Add(command, "name", name);
                DbHelpers.Add(command, "owner_id", ownerId);
                Dialect.AddLeaseWindow(command, _timeProvider, duration);
            },
            (command, token) => DbHelpers.ExecuteScalarAsync(command, token),
            cancellationToken).ConfigureAwait(false);

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

        return await _context.ExecuteStatementAsync(
            _sql.RenewSingletonLease,
            command =>
            {
                DbHelpers.Add(command, "name", name);
                DbHelpers.Add(command, "owner_id", ownerId);
                Dialect.AddLeaseWindow(command, _timeProvider, duration);
            },
            DbHelpers.ExecuteAsync,
            cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Same rationale as TryAcquireAsync: single-executor election is a deployment-wide concept.")]
    public async ValueTask ReleaseAsync(string name, string ownerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        await _context.ExecuteStatementAsync(
            _sql.ReleaseSingletonLease,
            command =>
            {
                DbHelpers.Add(command, "name", name);
                DbHelpers.Add(command, "owner_id", ownerId);
            },
            DbHelpers.ExecuteAsync,
            cancellationToken).ConfigureAwait(false);
    }

}
