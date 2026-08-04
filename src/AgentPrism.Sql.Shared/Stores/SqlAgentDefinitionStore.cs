using System.Data.Common;
using System.Text.Json;

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
internal sealed class SqlAgentDefinitionStore : IAgentDefinitionStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;
    private readonly ITenantContext _tenantContext;

    /// <summary>Yeni bir tanim deposu olusturur.</summary>
    /// <param name="context">Depo baglami.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public SqlAgentDefinitionStore(
        SqlStoreContext context,
        ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _context = context;
        _sql = context.Sql;
        _tenantContext = tenantContext;
    }

    /// <summary>Saglayiciya ozgu davranislarin kapisi.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask<AgentDefinition?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        var tenantId = _tenantContext.TenantId;

        var command = CreateCommand(_sql.SelectAgentDefinition);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "name", name);

        return await DbHelpers.ReadSingleAsync(
            command,
            reader => ReadDefinition(
                reader.GetString(0),
                name,
                reader.GetInt32(1),
                tenantId,
                DbHelpers.GetTimestamp(reader, 2)),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<AgentDefinition?> GetVersionAsync(string name, int version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        var tenantId = _tenantContext.TenantId;

        var command = CreateCommand(_sql.SelectAgentDefinitionVersion);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "name", name);
        DbHelpers.Add(command, "version", version);

        return await DbHelpers.ReadSingleAsync(
            command,
            reader => ReadDefinition(
                reader.GetString(0),
                name,
                version,
                tenantId,
                DbHelpers.GetTimestamp(reader, 1)),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AgentDefinition>> ListAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;

        var command = CreateCommand(_sql.SelectAgentDefinitions);
        DbHelpers.Add(command, "tenant_id", tenantId);

        return await DbHelpers.ReadListAsync(
            command,
            reader => ReadDefinition(
                reader.GetString(1),
                reader.GetString(0),
                reader.GetInt32(2),
                tenantId,
                DbHelpers.GetTimestamp(reader, 3)),
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
        DbHelpers.Add(command, "tenant_id", _tenantContext.TenantId);
        DbHelpers.Add(command, "name", name);

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AgentDefinition>> ListVersionsAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        var tenantId = _tenantContext.TenantId;

        var command = CreateCommand(_sql.SelectAgentDefinitionVersions);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "name", name);

        return await DbHelpers.ReadListAsync(
            command,
            reader => ReadDefinition(
                reader.GetString(0),
                name,
                reader.GetInt32(1),
                tenantId,
                DbHelpers.GetTimestamp(reader, 2)),
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
        DbHelpers.Add(command, "tenant_id", _tenantContext.TenantId);
        DbHelpers.Add(command, "name", name);
        DbHelpers.Add(command, "version", version);

        var result = await DbHelpers.ExecuteScalarAsync(command, cancellationToken).ConfigureAwait(false);

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

        var connection = await _context.DataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                var upsert = _context.CreateCommand(_sql.UpsertAgentDefinition, connection, transaction);

                DbHelpers.Add(upsert, "id", AgentPrismId.NewId());
                DbHelpers.Add(upsert, "tenant_id", tenantId);
                DbHelpers.Add(upsert, "name", name);
                Dialect.AddJsonb(upsert, "definition", payload);
                Dialect.AddTimestamp(upsert, "now", now);

                var written = await DbHelpers
                    .ReadSingleAsync(upsert, static reader => new WrittenVersion(reader.GetGuid(0), reader.GetInt32(1)), cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw new AgentPrismException($"'{name}' agent tanimi kaydedilemedi: veritabani surum bilgisi dondurmedi.");

                var insertVersion = _context.CreateCommand(_sql.InsertAgentDefinitionVersion, connection, transaction);

                DbHelpers.Add(insertVersion, "id", AgentPrismId.NewId());
                DbHelpers.Add(insertVersion, "agent_id", written.AgentId);
                DbHelpers.Add(insertVersion, "version", written.Version);
                Dialect.AddJsonb(insertVersion, "definition", payload);
                Dialect.AddText(insertVersion, "created_by", null);
                Dialect.AddTimestamp(insertVersion, "created_at", now);

                await DbHelpers.ExecuteAsync(insertVersion, cancellationToken).ConfigureAwait(false);
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

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

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
