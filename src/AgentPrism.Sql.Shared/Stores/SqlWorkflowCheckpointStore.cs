using System.Data.Common;
using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// Stores workflow checkpoints in the SQL database.
/// </summary>
/// <remarks>
/// <para>
/// State is stored in the <c>json</c> column, <strong>not</strong> the <c>jsonb</c>
/// column. Microsoft Agent Framework's checkpoint payload is polymorphic and the
/// <c>$type</c> discriminator must be the first property of the object it is in;
/// <c>jsonb</c> breaks this rule by reordering keys.
/// </para>
/// <para>
/// The parameter is also marked as <c>json</c>: the default text send format can fail
/// to write directly to the server's column type.
/// </para>
/// </remarks>
internal sealed class SqlWorkflowCheckpointStore : IWorkflowCheckpointStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Creates a new checkpoint store.</summary>
    /// <param name="context">The store context.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public SqlWorkflowCheckpointStore(
        SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    /// <summary>The gateway for provider-specific behavior.</summary>
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
        Dialect.AddInt32(command, "state_schema_version", record.StateSchemaVersion);
        Dialect.AddText(command, "state_maf_version", record.StateMafVersion);
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

        // The document must not outlive the caller: when a JsonDocument is
        // disposed it returns its buffer, and a JsonElement taken from it
        // becomes invalid. Clone() copies the buffer and makes the value independent.
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
    /// Reads a metadata row. <see cref="WorkflowCheckpointRecord.State"/> is set
    /// to an empty object: the list query deliberately does not select the state payload.
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
            StateSchemaVersion = reader.IsDBNull(7) ? null : reader.GetInt32(7),
            StateMafVersion = DbHelpers.GetNullableString(reader, 8),
            State = WorkflowCheckpointState.Omitted,
        };
}
