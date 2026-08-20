using System.Data.Common;

namespace AgentPrism;

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

        var command = CreateCommand(_sql.SelectTenantEgressPolicy);
        DbHelpers.Add(command, "tenant_id", tenantId);

        return await DbHelpers.ReadSingleAsync(command, ReadRecord, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<TenantEgressPolicy> UpsertAsync(string tenantId, IReadOnlyList<string> allowedProviders, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(allowedProviders);

        var updatedAt = _timeProvider.GetUtcNow();

        var command = CreateCommand(_sql.UpsertTenantEgressPolicy);
        DbHelpers.Add(command, "tenant_id", tenantId);
        Dialect.AddTextArray(command, "allowed_providers", allowedProviders);
        Dialect.AddTimestamp(command, "updated_at", updatedAt);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);

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

        var command = CreateCommand(_sql.DeleteTenantEgressPolicy);
        DbHelpers.Add(command, "tenant_id", tenantId);

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    private TenantEgressPolicy ReadRecord(DbDataReader reader)
        => new()
        {
            TenantId = reader.GetString(0),
            AllowedProviders = Dialect.ReadTextArray(reader, 1),
            UpdatedAt = DbHelpers.GetTimestamp(reader, 2),
        };
}
