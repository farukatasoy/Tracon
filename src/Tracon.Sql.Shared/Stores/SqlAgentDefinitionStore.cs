using System.Data.Common;
using System.Text.Json;

namespace Tracon;

/// <summary>
/// Stores agent definitions in the SQL database.
/// </summary>
/// <remarks>
/// <para>
/// The behavior contract is identical to <see cref="InMemoryAgentDefinitionStore"/>:
/// every save increments the version, history is never deleted, and a rollback
/// writes the old version back as a new version. A behavior difference between
/// the two implementations is a defect and is guarded by the shared contract tests.
/// </para>
/// <para>
/// All operations are scoped by <see cref="ITenantContext.TenantId"/>. A tenant
/// cannot see another tenant's definition.
/// </para>
/// </remarks>
internal sealed class SqlAgentDefinitionStore : IAgentDefinitionStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;
    private readonly ITenantContext _tenantContext;

    /// <summary>Creates a new definition store.</summary>
    /// <param name="context">The store context.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
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

    /// <summary>The gateway for provider-specific behavior.</summary>
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
            TraconJsonContext.Default.AgentDefinitionPayload);

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

        // A rollback does NOT delete the old version; it saves its content as a new version.
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

                DbHelpers.Add(upsert, "id", TraconId.NewId());
                DbHelpers.Add(upsert, "tenant_id", tenantId);
                DbHelpers.Add(upsert, "name", name);
                Dialect.AddJsonb(upsert, "definition", payload);
                Dialect.AddTimestamp(upsert, "now", now);

                var written = await DbHelpers
                    .ReadSingleAsync(upsert, static reader => new WrittenVersion(reader.GetGuid(0), reader.GetInt32(1)), cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw new TraconException($"Could not save the '{name}' agent definition: the database did not return version information.");

                var insertVersion = _context.CreateCommand(_sql.InsertAgentDefinitionVersion, connection, transaction);

                DbHelpers.Add(insertVersion, "id", TraconId.NewId());
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

    private async ValueTask<TraconException> BuildRollbackFailureAsync(
        string name,
        int version,
        CancellationToken cancellationToken)
    {
        var current = await GetAsync(name, cancellationToken).ConfigureAwait(false);

        return current is null
            ? new TraconException($"No agent definition named '{name}' was found.")
            : new TraconException($"Version {version} of the '{name}' agent was not found.");
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    private static AgentDefinition ReadDefinition(
        string payload,
        string name,
        int version,
        string tenantId,
        DateTimeOffset updatedAt)
    {
        var deserialized = JsonSerializer.Deserialize(payload, TraconJsonContext.Default.AgentDefinitionPayload)
            ?? throw new TraconException($"Could not read the '{name}' agent definition: the JSON payload in the database is empty.");

        return deserialized.ToDefinition(name, version, tenantId, updatedAt);
    }

    private sealed record WrittenVersion(Guid AgentId, int Version);
}
