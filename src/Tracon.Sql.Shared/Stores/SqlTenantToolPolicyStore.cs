using System.Data.Common;

namespace Tracon;

/// <summary>SQL-backed store for per-tenant tool policies.</summary>
internal sealed class SqlTenantToolPolicyStore : ITenantToolPolicyStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a new tenant tool policy store.</summary>
    /// <param name="context">The store context.</param>
    /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/> when not given.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public SqlTenantToolPolicyStore(SqlStoreContext context, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask<TenantToolPolicy?> GetAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectTenantToolPolicy);
        DbHelpers.AddTenant(command, tenantId);

        return await DbHelpers.ReadSingleAsync(command, ReadRecord, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<TenantToolPolicy> UpsertAsync(
        string tenantId,
        IReadOnlyList<string> allowedTools,
        IReadOnlyList<string> grantedPermissions,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(allowedTools);
        ArgumentNullException.ThrowIfNull(grantedPermissions);

        var updatedAt = _timeProvider.GetUtcNow();

        await _context.ExecuteUpsertAsync(
            _sql.UpsertTenantToolPolicy,
            command =>
            {
                DbHelpers.AddTenant(command, tenantId);
                Dialect.AddTextArray(command, "allowed_tools", allowedTools);
                Dialect.AddTextArray(command, "granted_permissions", grantedPermissions);
                Dialect.AddTimestamp(command, "updated_at", updatedAt);
            },
            (command, token) => DbHelpers.ExecuteAsync(command, token),
            cancellationToken).ConfigureAwait(false);

        return new TenantToolPolicy
        {
            TenantId = AmbientTenantScope.Normalize(tenantId),
            AllowedTools = allowedTools,
            GrantedPermissions = grantedPermissions,
            UpdatedAt = updatedAt,
        };
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.DeleteTenantToolPolicy);
        DbHelpers.AddTenant(command, tenantId);

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    private TenantToolPolicy ReadRecord(DbDataReader reader)
        => new()
        {
            TenantId = reader.GetString(0),
            AllowedTools = Dialect.ReadTextArray(reader, 1),
            GrantedPermissions = Dialect.ReadTextArray(reader, 2),
            UpdatedAt = DbHelpers.GetTimestamp(reader, 3),
        };
}
