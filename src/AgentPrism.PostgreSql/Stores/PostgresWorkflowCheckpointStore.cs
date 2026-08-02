using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace AgentPrism;

/// <summary>Workflow kontrol noktalarini PostgreSQL'de saklayan depo.</summary>
/// <remarks>
/// <para>
/// 🚨 Durum <c>json</c> sutununda saklanir, <c>jsonb</c> sutununda
/// <strong>degil</strong>. Microsoft Agent Framework'un kontrol noktasi yuku
/// polimorfiktir ve <c>$type</c> ayraci bulundugu nesnenin ilk ozelligi olmak
/// zorundadir; <c>jsonb</c> anahtarlari yeniden siralayarak bu kurali bozar.
/// Karar K-027, olcum <c>docs/15-WORKFLOWS-YURUTME.md</c>.
/// </para>
/// <para>
/// Parametre de <see cref="NpgsqlDbType.Json"/> olarak isaretlenir: varsayilan
/// metin gonderimi sunucunun sutun tipine dogrudan yazamamasina yol acabilir.
/// </para>
/// </remarks>
public sealed class PostgresWorkflowCheckpointStore : IWorkflowCheckpointStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly SqlQueries _sql;
    private readonly int _commandTimeout;

    /// <summary>Yeni bir kontrol noktasi deposu olusturur.</summary>
    /// <param name="dataSource">Veri kaynagi.</param>
    /// <param name="options">PostgreSQL ayarlari.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public PostgresWorkflowCheckpointStore(
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
    public async ValueTask<WorkflowCheckpointRecord> CreateAsync(
        WorkflowCheckpointRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var command = CreateCommand(_sql.InsertWorkflowCheckpoint);
        command.Parameters.AddWithValue("id", record.Id);
        command.Parameters.AddWithValue("tenant_id", record.TenantId);
        command.Parameters.AddWithValue("session_id", record.SessionId);
        command.Parameters.AddWithValue("checkpoint_id", record.CheckpointId);
        command.Parameters.AddWithValue("parent_id", (object?)record.ParentCheckpointId ?? DBNull.Value);
        command.Parameters.AddWithValue("run_id", (object?)record.RunId ?? DBNull.Value);
        command.Parameters.Add(new NpgsqlParameter("state", NpgsqlDbType.Json)
        {
            Value = record.State.GetRawText(),
        });
        command.Parameters.AddWithValue("created_at", record.CreatedAt.UtcDateTime);

        await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);

        return record;
    }

    /// <inheritdoc />
    public async ValueTask<JsonElement?> ReadAsync(
        string tenantId,
        string sessionId,
        string checkpointId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);

        var command = CreateCommand(_sql.SelectWorkflowCheckpoint);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("checkpoint_id", checkpointId);

        var raw = await NpgsqlHelpers
            .ReadSingleAsync(command, static reader => reader.GetString(0), cancellationToken)
            .ConfigureAwait(false);

        // Belge cagiranin omrunu asmalidir: JsonDocument birakildiginda kendi
        // tamponunu geri verir ve icinden alinan JsonElement gecersizlesir.
        // Clone() tamponu kopyalar ve degeri bagimsiz kilar.
        if (raw is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(raw);

        return document.RootElement.Clone();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<WorkflowCheckpointRecord>> ListAsync(
        string tenantId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var command = CreateCommand(_sql.SelectWorkflowCheckpoints);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("session_id", sessionId);

        return await NpgsqlHelpers.ReadListAsync(command, ReadMetadata, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<WorkflowCheckpointRecord>> ListByRunAsync(
        string tenantId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectWorkflowCheckpointsByRun);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("run_id", runId);

        return await NpgsqlHelpers.ReadListAsync(command, ReadMetadata, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<int> DeleteAsync(
        string tenantId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var command = CreateCommand(_sql.DeleteWorkflowCheckpoints);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("session_id", sessionId);

        return await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private NpgsqlCommand CreateCommand(string sql)
    {
        var command = _dataSource.CreateCommand(sql);
        command.CommandTimeout = _commandTimeout;

        return command;
    }

    /// <summary>
    /// Ustveri satirini okur. <see cref="WorkflowCheckpointRecord.State"/> bos bir
    /// nesneye ayarlanir: liste sorgusu durum yukunu bilerek secmez.
    /// </summary>
    private static WorkflowCheckpointRecord ReadMetadata(NpgsqlDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            SessionId = reader.GetString(2),
            CheckpointId = reader.GetString(3),
            ParentCheckpointId = NpgsqlHelpers.GetNullableString(reader, 4),
            RunId = reader.IsDBNull(5) ? null : reader.GetGuid(5),
            CreatedAt = NpgsqlHelpers.GetTimestamp(reader, 6),
            State = WorkflowCheckpointState.Omitted,
        };
}
