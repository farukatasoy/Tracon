using System.Data.Common;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>Stores run inputs in the SQL database (Phase 47).</summary>
/// <remarks>
/// <para>
/// The behavior contract is identical to <see cref="InMemoryRunInputStore"/>
/// and is guarded by the shared contract tests.
/// </para>
/// <para>
/// 🚨 Messages are stored in the <c>json</c> column, <strong>not</strong>
/// <c>jsonb</c>: <c>ChatMessage</c> content is polymorphic and the <c>$type</c>
/// discriminator must be the object's first property (K-027). The column type
/// is fixed by the schema, not by <see cref="SqlDialect.AddJson"/>; the only
/// job here is serializing the text correctly.
/// </para>
/// </remarks>
internal sealed class SqlRunInputStore : IRunInputStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Creates a new SQL input store.</summary>
    /// <param name="context">The store context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public SqlRunInputStore(SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask SaveAsync(RunInputRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var command = CreateCommand(_sql.InsertRunInput);
        Dialect.AddUuid(command, "run_id", record.RunId);
        DbHelpers.Add(command, "tenant_id", record.TenantId);
        Dialect.AddJson(
            command,
            "messages",
            JsonSerializer.Serialize(record.Messages, AgentPrismJsonContext.Default.IReadOnlyListChatMessage));
        Dialect.AddTimestamp(command, "created_at", record.CreatedAt);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<RunInputRecord?> GetAsync(
        string tenantId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectRunInput);
        Dialect.AddUuid(command, "run_id", runId);
        DbHelpers.Add(command, "tenant_id", tenantId);

        return await DbHelpers
            .ReadSingleAsync(command, reader => Read(reader, tenantId, runId), cancellationToken)
            .ConfigureAwait(false);
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    private static RunInputRecord Read(DbDataReader reader, string tenantId, Guid runId)
        => new()
        {
            RunId = runId,
            TenantId = tenantId,
            Messages = JsonSerializer.Deserialize(
                reader.GetString(0),
                AgentPrismJsonContext.Default.IReadOnlyListChatMessage) ?? [],
            CreatedAt = DbHelpers.GetTimestamp(reader, 1),
        };
}
