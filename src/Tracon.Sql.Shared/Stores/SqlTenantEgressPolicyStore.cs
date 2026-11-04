using System.Data.Common;

namespace Tracon;

/// <summary>SQL-backed store for per-tenant model provider egress policies.</summary>
internal sealed class SqlTenantEgressPolicyStore : ITenantEgressPolicyStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a new tenant egress policy store.</summary>
    /// <param name="context">The store context.</param>
    /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/> when not given.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public SqlTenantEgressPolicyStore(SqlStoreContext context, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask<TenantEgressPolicy?> GetAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        return await _context.ExecuteStatementAsync(
            _sql.SelectTenantEgressPolicy,
            command =>
            {
                DbHelpers.AddTenant(command, tenantId);
            },
            (command, token) => DbHelpers.ReadSingleAsync(command, ReadRecord, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<TenantEgressPolicy> UpsertAsync(string tenantId, IReadOnlyList<string> allowedProviders, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(allowedProviders);

        // Folded here, not only inside AddTenant: the record is built from this value, and it must carry the
        // canonical form whichever spelling the caller wrote (a later read answers with the row's tenant).
        tenantId = AmbientTenantScope.Normalize(tenantId);

        var updatedAt = _timeProvider.GetUtcNow();

        await _context.ExecuteUpsertAsync(
            _sql.UpsertTenantEgressPolicy,
            command =>
            {
                DbHelpers.AddTenant(command, tenantId);
                Dialect.AddTextArray(command, "allowed_providers", allowedProviders);
                Dialect.AddTimestamp(command, "updated_at", updatedAt);
            },
            (command, token) => DbHelpers.ExecuteAsync(command, token),
            cancellationToken).ConfigureAwait(false);

        return new TenantEgressPolicy
        {
            TenantId = tenantId,
            AllowedProviders = allowedProviders,
            UpdatedAt = updatedAt,
        };
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        return await _context.ExecuteStatementAsync(
            _sql.DeleteTenantEgressPolicy,
            command =>
            {
                DbHelpers.AddTenant(command, tenantId);
            },
            DbHelpers.ExecuteAsync,
            cancellationToken).ConfigureAwait(false) > 0;
    }

    private TenantEgressPolicy ReadRecord(DbDataReader reader)
        => new()
        {
            TenantId = reader.GetString(0),
            AllowedProviders = Dialect.ReadTextArray(reader, 1),
            UpdatedAt = DbHelpers.GetTimestamp(reader, 2),
        };
}
