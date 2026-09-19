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

        var now = DateTimeOffset.UtcNow;

        var command = CreateCommand(_sql.UpsertWorkflow);
        DbHelpers.Add(command, "id", TraconId.NewId());
        DbHelpers.AddTenant(command, tenantId);
        DbHelpers.Add(command, "name", definition.Name);
        Dialect.AddJsonb(command, "definition", payload);
        Dialect.AddTimestamp(command, "now", now);

        var written = await DbHelpers.ReadSingleAsync(
                command,
                static reader => new WrittenVersion(reader.GetInt32(0), DbHelpers.GetTimestamp(reader, 1)),
                cancellationToken).ConfigureAwait(false)
            ?? throw new TraconException(
                $"Failed to save workflow definition '{definition.Name}': the database did not return version information.");

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
        DbHelpers.AddTenant(command, tenantId);
        DbHelpers.Add(command, "name", name);

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
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
