using System.Data.Common;
using System.Text.Json;

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
/// Parametre de <c>json</c> olarak isaretlenir: varsayilan
/// metin gonderimi sunucunun sutun tipine dogrudan yazamamasina yol acabilir.
/// </para>
/// </remarks>
internal sealed class SqlWorkflowCheckpointStore : IWorkflowCheckpointStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Yeni bir kontrol noktasi deposu olusturur.</summary>
    /// <param name="context">Depo baglami.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public SqlWorkflowCheckpointStore(
        SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    /// <summary>Saglayiciya ozgu davranislarin kapisi.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask<WorkflowCheckpointRecord> CreateAsync(
        WorkflowCheckpointRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var command = CreateCommand(_sql.InsertWorkflowCheckpoint);
        DbHelpers.Add(command, "id", record.Id);
        DbHelpers.Add(command, "tenant_id", record.TenantId);
        DbHelpers.Add(command, "session_id", record.SessionId);
        DbHelpers.Add(command, "checkpoint_id", record.CheckpointId);
        Dialect.AddText(command, "parent_id", record.ParentCheckpointId);
        Dialect.AddUuid(command, "run_id", record.RunId);
        Dialect.AddJson(command, "state", record.State.GetRawText());
        Dialect.AddTimestamp(command, "created_at", record.CreatedAt);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);

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
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "session_id", sessionId);
        DbHelpers.Add(command, "checkpoint_id", checkpointId);

        var raw = await DbHelpers
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
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "session_id", sessionId);

        return await DbHelpers.ReadListAsync(command, ReadMetadata, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<WorkflowCheckpointRecord>> ListByRunAsync(
        string tenantId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectWorkflowCheckpointsByRun);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "run_id", runId);

        return await DbHelpers.ReadListAsync(command, ReadMetadata, cancellationToken).ConfigureAwait(false);
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
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "session_id", sessionId);

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    /// <summary>
    /// Ustveri satirini okur. <see cref="WorkflowCheckpointRecord.State"/> bos bir
    /// nesneye ayarlanir: liste sorgusu durum yukunu bilerek secmez.
    /// </summary>
    private static WorkflowCheckpointRecord ReadMetadata(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            SessionId = reader.GetString(2),
            CheckpointId = reader.GetString(3),
            ParentCheckpointId = DbHelpers.GetNullableString(reader, 4),
            RunId = reader.IsDBNull(5) ? null : reader.GetGuid(5),
            CreatedAt = DbHelpers.GetTimestamp(reader, 6),
            State = WorkflowCheckpointState.Omitted,
        };
}
