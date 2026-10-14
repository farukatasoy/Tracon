using System.Data.Common;
using System.Text.Json;

namespace Tracon;

/// <summary>Stores workflow definitions in the SQL database.</summary>
/// <remarks>
/// <para>
/// The behavior contract is identical to <c>InMemoryWorkflowDefinitionStore</c>
/// and is protected by shared contract tests.
/// </para>
/// <para>
/// <strong>Every save writes two rows in one transaction:</strong> the current definition
/// (<c>workflows</c>) and its history entry (<c>workflow_definition_versions</c>). A save whose
/// history write fails leaves the current row as it was; neither a version without content nor
/// content without a version can be left behind. The history has no foreign key to the current
/// row, so deleting a workflow keeps its versions and a later save continues the numbering.
/// </para>
/// <para>
/// The tenant id is taken <em>as a parameter</em>, not read from <c>ITenantContext</c>.
/// The checkpoint store and the workflow store are also called from
/// MAF's execution pipeline; there is no HTTP context there, and the ambient
/// tenant would fall back to the wrong value. The same choice was made for
/// <see cref="SqlAgentSkillStore"/>.
/// </para>
/// </remarks>
internal sealed class SqlWorkflowDefinitionStore : IWorkflowDefinitionStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Creates a new workflow definition store.</summary>
    /// <param name="context">The store context.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public SqlWorkflowDefinitionStore(
        SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    /// <summary>The gateway for provider-specific behavior.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask<WorkflowDefinition?> GetAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var command = CreateCommand(_sql.SelectWorkflow);
        DbHelpers.AddTenant(command, tenantId);
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
    public async ValueTask<IReadOnlyList<WorkflowDefinition>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectWorkflows);
        DbHelpers.AddTenant(command, tenantId);

        // The name lives in the column, NOT in the `definition` payload; when
        // reading, it must come from the column, not the JSON. Since the
        // query does not select by name, it is the only field here not
        // embedded in the payload, and has been added to the SELECT list
        // instead of a separate query.
        return await DbHelpers.ReadListAsync(
            command,
            reader => ReadDefinition(
                reader.GetString(0),
                reader.GetString(3),
                reader.GetInt32(1),
                tenantId,
                DbHelpers.GetTimestamp(reader, 2)),
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
            TraconJsonContext.Default.WorkflowDefinitionPayload);

        return await WriteVersionAsync(tenantId, definition.Name, payload, cancellationToken).ConfigureAwait(false);
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
        DbHelpers.AddTenant(command, tenantId);
        DbHelpers.Add(command, "name", name);

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    public async ValueTask<WorkflowDefinition?> GetVersionAsync(
        string tenantId,
        string name,
        int version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var command = CreateCommand(_sql.SelectWorkflowVersion);
        DbHelpers.AddTenant(command, tenantId);
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
    public async ValueTask<IReadOnlyList<WorkflowDefinition>> ListVersionsAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var command = CreateCommand(_sql.SelectWorkflowVersions);
        DbHelpers.AddTenant(command, tenantId);
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
    public async ValueTask<WorkflowDefinition> RollbackAsync(
        string tenantId,
        string name,
        int version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var command = CreateCommand(_sql.SelectWorkflowVersion);
        DbHelpers.AddTenant(command, tenantId);
        DbHelpers.Add(command, "name", name);
        DbHelpers.Add(command, "version", version);

        var payload = await DbHelpers.ReadSingleAsync(
            command,
            static reader => reader.GetString(0),
            cancellationToken).ConfigureAwait(false)
            ?? throw new TraconException($"Version {version} of the '{name}' workflow definition was not found.");

        // A rollback does NOT delete or move anything; it saves the old content as a new version.
        return await WriteVersionAsync(tenantId, name, payload, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the current row and the history entry together, in one transaction.
    /// </summary>
    /// <remarks>
    /// The upsert decides the number (one more than the row, or one more than the highest
    /// version in the history when the row is gone) and the history insert uses what the
    /// upsert returned. Two concurrent saves of one name serialize on the row, so they get
    /// two numbers; a collision in the history rolls the whole save back. A deadlock victim
    /// is run again whole by the store context.
    /// </remarks>
    private async ValueTask<WorkflowDefinition> WriteVersionAsync(
        string tenantId,
        string name,
        string payload,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        return await _context.ExecuteInTransactionAsync(
            async (connection, transaction) =>
            {
                var upsert = _context.CreateCommand(_sql.UpsertWorkflow, connection, transaction);

                DbHelpers.Add(upsert, "id", TraconId.NewId());
                DbHelpers.AddTenant(upsert, tenantId);
                DbHelpers.Add(upsert, "name", name);
                Dialect.AddJsonb(upsert, "definition", payload);
                Dialect.AddTimestamp(upsert, "now", now);

                var written = await DbHelpers.ReadSingleAsync(
                    upsert,
                    static reader => new WrittenVersion(reader.GetInt32(0), DbHelpers.GetTimestamp(reader, 1)),
                    cancellationToken).ConfigureAwait(false)
                    ?? throw new TraconException(
                        $"Failed to save workflow definition '{name}': the database did not return version information.");

                var history = _context.CreateCommand(_sql.InsertWorkflowVersion, connection, transaction);

                DbHelpers.AddTenant(history, tenantId);
                DbHelpers.Add(history, "name", name);
                DbHelpers.Add(history, "version", written.Version);
                Dialect.AddJsonb(history, "definition", payload);
                Dialect.AddTimestamp(history, "created_at", written.UpdatedAt);

                await DbHelpers.ExecuteAsync(history, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                return ReadDefinition(payload, name, written.Version, tenantId, written.UpdatedAt);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    private static WorkflowDefinition ReadDefinition(
        string payload,
        string name,
        int version,
        string tenantId,
        DateTimeOffset updatedAt)
    {
        var deserialized = JsonSerializer.Deserialize(payload, TraconJsonContext.Default.WorkflowDefinitionPayload)
            ?? throw new TraconException($"Failed to read workflow definition '{name}': the JSON payload in the database is empty.");

        return deserialized.ToDefinition(name, version, tenantId, updatedAt);
    }

    private sealed record WrittenVersion(int Version, DateTimeOffset UpdatedAt);
}
