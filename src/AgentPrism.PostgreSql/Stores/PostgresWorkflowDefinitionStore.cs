using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace AgentPrism;

/// <summary>Workflow tanimlarini PostgreSQL'de saklayan depo.</summary>
/// <remarks>
/// <para>
/// Davranis sozlesmesi <see cref="InMemoryWorkflowDefinitionStore"/> ile birebir
/// aynidir ve ortak sozlesme testleriyle korunur.
/// </para>
/// <para>
/// Kiraci kimligi <em>parametre olarak</em> alinir, <c>ITenantContext</c>'ten
/// okunmaz. Sebep: kontrol noktasi deposu ve workflow deposu MAF'in yurutme
/// hattindan da cagrilir; orada HTTP baglami yoktur ve ortam kiracisi yanlis
/// degere duserdi. Ayni tercih <see cref="PostgresAgentSkillStore"/> icin de
/// yapilmisti.
/// </para>
/// </remarks>
public sealed class PostgresWorkflowDefinitionStore : IWorkflowDefinitionStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly SqlQueries _sql;
    private readonly int _commandTimeout;

    /// <summary>Yeni bir workflow tanim deposu olusturur.</summary>
    /// <param name="dataSource">Veri kaynagi.</param>
    /// <param name="options">PostgreSQL ayarlari.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public PostgresWorkflowDefinitionStore(
        NpgsqlDataSource dataSource,
        IOptions<AgentPrismPostgreSqlOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);

        _dataSource = dataSource;
        _sql = new SqlQueries(options.Value.SchemaName);
        _commandTimeout = options.Value.CommandTimeoutSeconds;
    }

    /// <inheritdoc />
    public async ValueTask<WorkflowDefinition?> GetAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var command = CreateCommand(_sql.SelectWorkflow);
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
    public async ValueTask<IReadOnlyList<WorkflowDefinition>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectWorkflows);
        command.Parameters.AddWithValue("tenant_id", tenantId);

        // Ad `definition` yukunde DEGIL, sutunda yasar; okurken JSON'dan degil
        // sutundan alinmasi gerekir. Sorgu adi secmedigi icin burada yuke
        // gomulu olmayan tek alan odur ve ayri bir sorgu yerine SELECT
        // listesine eklenmistir.
        return await NpgsqlHelpers.ReadListAsync(
            command,
            reader => ReadDefinition(
                reader.GetString(0),
                reader.GetString(3),
                reader.GetInt32(1),
                tenantId,
                NpgsqlHelpers.GetTimestamp(reader, 2)),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<WorkflowDefinition> SaveAsync(
        string tenantId,
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(definition);

        var payload = JsonSerializer.Serialize(
            WorkflowDefinitionPayload.FromDefinition(definition),
            AgentPrismJsonContext.Default.WorkflowDefinitionPayload);

        var now = DateTimeOffset.UtcNow;

        var command = CreateCommand(_sql.UpsertWorkflow);
        command.Parameters.AddWithValue("id", AgentPrismId.NewId());
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("name", definition.Name);
        command.Parameters.Add(new NpgsqlParameter("definition", NpgsqlDbType.Jsonb) { Value = payload });
        command.Parameters.AddWithValue("now", now.UtcDateTime);

        var written = await NpgsqlHelpers.ReadSingleAsync(
                command,
                static reader => new WrittenVersion(reader.GetInt32(0), NpgsqlHelpers.GetTimestamp(reader, 1)),
                cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismException(
                $"'{definition.Name}' workflow tanimi kaydedilemedi: veritabani surum bilgisi dondurmedi.");

        return definition with
        {
            TenantId = tenantId,
            Version = written.Version,
            UpdatedAt = written.UpdatedAt,
        };
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var command = CreateCommand(_sql.DeleteWorkflow);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("name", name);

        return await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    private NpgsqlCommand CreateCommand(string sql)
    {
        var command = _dataSource.CreateCommand(sql);
        command.CommandTimeout = _commandTimeout;

        return command;
    }

    private static WorkflowDefinition ReadDefinition(
        string payload,
        string name,
        int version,
        string tenantId,
        DateTimeOffset updatedAt)
    {
        var deserialized = JsonSerializer.Deserialize(payload, AgentPrismJsonContext.Default.WorkflowDefinitionPayload)
            ?? throw new AgentPrismException($"'{name}' workflow tanimi okunamadi: veritabanindaki JSON yuku bos.");

        return deserialized.ToDefinition(name, version, tenantId, updatedAt);
    }

    private sealed record WrittenVersion(int Version, DateTimeOffset UpdatedAt);
}
