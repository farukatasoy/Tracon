using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace AgentPrism;

/// <summary>
/// Agent tanimlarini PostgreSQL'de saklayan depo.
/// </summary>
/// <remarks>
/// <para>
/// Davranis sozlesmesi <see cref="InMemoryAgentDefinitionStore"/> ile birebir aynidir:
/// her kayit surumu artirir, gecmis silinmez, geri alma eski surumu yeni surum olarak
/// yazar. Iki uygulama arasindaki davranis farki hatadir ve ortak sozlesme testleriyle
/// korunur.
/// </para>
/// <para>
/// Tum islemler <see cref="ITenantContext.TenantId"/> ile sinirlidir. Bir kiraci
/// digerinin tanimini goremez.
/// </para>
/// </remarks>
public sealed class PostgresAgentDefinitionStore : IAgentDefinitionStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly SqlQueries _sql;
    private readonly ITenantContext _tenantContext;
    private readonly int _commandTimeout;

    /// <summary>Yeni bir tanim deposu olusturur.</summary>
    /// <param name="dataSource">Veri kaynagi.</param>
    /// <param name="options">PostgreSQL ayarlari.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public PostgresAgentDefinitionStore(
        NpgsqlDataSource dataSource,
        IOptions<AgentPrismPostgreSqlOptions> options,
        ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _dataSource = dataSource;
        _sql = new SqlQueries(options.Value.SchemaName);
        _tenantContext = tenantContext;
        _commandTimeout = options.Value.CommandTimeoutSeconds;
    }

    /// <inheritdoc />
    public async ValueTask<AgentDefinition?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        var tenantId = _tenantContext.TenantId;

        var command = CreateCommand(_sql.SelectAgentDefinition);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("name", name);

        return await NpgsqlHelpers.ReadSingleAsync(
            command,
            reader => ReadDefinition(
                reader.GetString(0),
                name,
                reader.GetInt32(1),
                tenantId,
                NpgsqlHelpers.GetTimestamp(reader, 2)),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<AgentDefinition?> GetVersionAsync(string name, int version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        var tenantId = _tenantContext.TenantId;

        var command = CreateCommand(_sql.SelectAgentDefinitionVersion);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("version", version);

        return await NpgsqlHelpers.ReadSingleAsync(
            command,
            reader => ReadDefinition(
                reader.GetString(0),
                name,
                version,
                tenantId,
                NpgsqlHelpers.GetTimestamp(reader, 1)),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AgentDefinition>> ListAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;

        var command = CreateCommand(_sql.SelectAgentDefinitions);
        command.Parameters.AddWithValue("tenant_id", tenantId);

        return await NpgsqlHelpers.ReadListAsync(
            command,
            reader => ReadDefinition(
                reader.GetString(1),
                reader.GetString(0),
                reader.GetInt32(2),
                tenantId,
                NpgsqlHelpers.GetTimestamp(reader, 3)),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<AgentDefinition> SaveAsync(
        AgentDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var payload = JsonSerializer.Serialize(
            AgentDefinitionPayload.FromDefinition(definition),
            AgentPrismJsonContext.Default.AgentDefinitionPayload);

        return await WriteVersionAsync(definition.Name, payload, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        var command = CreateCommand(_sql.DeleteAgentDefinition);
        command.Parameters.AddWithValue("tenant_id", _tenantContext.TenantId);
        command.Parameters.AddWithValue("name", name);

        return await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AgentDefinition>> ListVersionsAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        var tenantId = _tenantContext.TenantId;

        var command = CreateCommand(_sql.SelectAgentDefinitionVersions);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("name", name);

        return await NpgsqlHelpers.ReadListAsync(
            command,
            reader => ReadDefinition(
                reader.GetString(0),
                name,
                reader.GetInt32(1),
                tenantId,
                NpgsqlHelpers.GetTimestamp(reader, 2)),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<AgentDefinition> RollbackAsync(
        string name,
        int version,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        var command = CreateCommand(_sql.SelectAgentDefinitionVersion);
        command.Parameters.AddWithValue("tenant_id", _tenantContext.TenantId);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("version", version);

        var result = await NpgsqlHelpers.ExecuteScalarAsync(command, cancellationToken).ConfigureAwait(false);

        if (result is not string payload)
        {
            throw await BuildRollbackFailureAsync(name, version, cancellationToken).ConfigureAwait(false);
        }

        // Geri alma eski surumu SILMEZ; icerigini yeni bir surum olarak kaydeder.
        return await WriteVersionAsync(name, payload, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<AgentDefinition> WriteVersionAsync(
        string name,
        string payload,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var now = DateTimeOffset.UtcNow;

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                var upsert = new NpgsqlCommand(_sql.UpsertAgentDefinition, connection, transaction)
                {
                    CommandTimeout = _commandTimeout,
                };

                upsert.Parameters.AddWithValue("id", AgentPrismId.NewId());
                upsert.Parameters.AddWithValue("tenant_id", tenantId);
                upsert.Parameters.AddWithValue("name", name);
                upsert.Parameters.Add(new NpgsqlParameter("definition", NpgsqlDbType.Jsonb) { Value = payload });
                upsert.Parameters.AddWithValue("now", now.UtcDateTime);

                var written = await NpgsqlHelpers
                    .ReadSingleAsync(upsert, static reader => new WrittenVersion(reader.GetGuid(0), reader.GetInt32(1)), cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw new AgentPrismException($"'{name}' agent tanimi kaydedilemedi: veritabani surum bilgisi dondurmedi.");

                var insertVersion = new NpgsqlCommand(_sql.InsertAgentDefinitionVersion, connection, transaction)
                {
                    CommandTimeout = _commandTimeout,
                };

                insertVersion.Parameters.AddWithValue("id", AgentPrismId.NewId());
                insertVersion.Parameters.AddWithValue("agent_id", written.AgentId);
                insertVersion.Parameters.AddWithValue("version", written.Version);
                insertVersion.Parameters.Add(new NpgsqlParameter("definition", NpgsqlDbType.Jsonb) { Value = payload });
                insertVersion.Parameters.AddWithValue("created_by", DBNull.Value);
                insertVersion.Parameters.AddWithValue("created_at", now.UtcDateTime);

                await NpgsqlHelpers.ExecuteAsync(insertVersion, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                return ReadDefinition(payload, name, written.Version, tenantId, now);
            }
        }
    }

    private async ValueTask<AgentPrismException> BuildRollbackFailureAsync(
        string name,
        int version,
        CancellationToken cancellationToken)
    {
        var current = await GetAsync(name, cancellationToken).ConfigureAwait(false);

        return current is null
            ? new AgentPrismException($"'{name}' adinda bir agent tanimi bulunamadi.")
            : new AgentPrismException($"'{name}' agent'inin {version} numarali surumu bulunamadi.");
    }

    private NpgsqlCommand CreateCommand(string sql)
    {
        var command = _dataSource.CreateCommand(sql);
        command.CommandTimeout = _commandTimeout;

        return command;
    }

    private static AgentDefinition ReadDefinition(
        string payload,
        string name,
        int version,
        string tenantId,
        DateTimeOffset updatedAt)
    {
        var deserialized = JsonSerializer.Deserialize(payload, AgentPrismJsonContext.Default.AgentDefinitionPayload)
            ?? throw new AgentPrismException($"'{name}' agent tanimi okunamadi: veritabanindaki JSON yuku bos.");

        return deserialized.ToDefinition(name, version, tenantId, updatedAt);
    }

    private sealed record WrittenVersion(Guid AgentId, int Version);
}
