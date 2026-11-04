using System.Data.Common;

namespace Tracon;

/// <summary>SQL-backed store for per-tenant model provider bindings (BYOK).</summary>
/// <remarks>
/// This store never writes or reads a secret value, only the
/// <strong>name</strong> of the configuration key the value is read from at
/// call time.
/// </remarks>
internal sealed class SqlTenantProviderBindingStore : ITenantProviderBindingStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a new tenant provider binding store.</summary>
    /// <param name="context">The store context.</param>
    /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/> when not given.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public SqlTenantProviderBindingStore(SqlStoreContext context, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask<TenantProviderBinding?> GetAsync(string tenantId, string providerName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        return await _context.ExecuteStatementAsync(
            _sql.SelectTenantProviderBinding,
            command =>
            {
                DbHelpers.AddTenant(command, tenantId);
                DbHelpers.Add(command, "provider_name", TenantProviderBinding.NormalizeProviderName(providerName));
            },
            (command, token) => DbHelpers.ReadSingleAsync(command, ReadRecord, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<TenantProviderBinding>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        return await _context.ExecuteStatementAsync(
            _sql.SelectTenantProviderBindings,
            command =>
            {
                DbHelpers.AddTenant(command, tenantId);
            },
            (command, token) => DbHelpers.ReadListAsync(command, ReadRecord, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask UpsertAsync(TenantProviderBinding binding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);

        await _context.ExecuteUpsertAsync(
            _sql.UpsertTenantProviderBinding,
            command =>
            {
                DbHelpers.AddTenant(command, binding.TenantId);
                DbHelpers.Add(command, "provider_name", TenantProviderBinding.NormalizeProviderName(binding.ProviderName));
                DbHelpers.Add(command, "api_key_configuration_name", binding.ApiKeyConfigurationName);
                Dialect.AddText(command, "endpoint", binding.Endpoint);
                Dialect.AddTimestamp(command, "updated_at", _timeProvider.GetUtcNow());
            },
            (command, token) => DbHelpers.ExecuteAsync(command, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(string tenantId, string providerName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        return await _context.ExecuteStatementAsync(
            _sql.DeleteTenantProviderBinding,
            command =>
            {
                DbHelpers.AddTenant(command, tenantId);
                DbHelpers.Add(command, "provider_name", TenantProviderBinding.NormalizeProviderName(providerName));
            },
            DbHelpers.ExecuteAsync,
            cancellationToken).ConfigureAwait(false) > 0;
    }

    private TenantProviderBinding ReadRecord(DbDataReader reader)
        => new()
        {
            TenantId = reader.GetString(0),
            ProviderName = reader.GetString(1),
            ApiKeyConfigurationName = reader.GetString(2),
            Endpoint = DbHelpers.GetNullableString(reader, 3),
            UpdatedAt = DbHelpers.GetTimestamp(reader, 4),
        };
}
