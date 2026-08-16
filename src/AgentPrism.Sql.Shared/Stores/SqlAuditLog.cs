using System.Data.Common;

namespace AgentPrism;

/// <summary>Ledger that stores audit trail records in the SQL database.</summary>
/// <remarks>
/// A write is matched by a read that goes through the <c>tenant_id, created_at</c>
/// index (<c>audit_log_tenant_created_idx</c>, set up in 0001). No migration is
/// needed; the schema has been sufficient since Phase 0.
/// </remarks>
internal sealed class SqlAuditLog : IAuditLog
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Creates a new audit trail ledger.</summary>
    /// <param name="context">The store context.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public SqlAuditLog(SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    /// <summary>The gateway for provider-specific behavior.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var command = CreateCommand(_sql.InsertAuditEntry);
        DbHelpers.Add(command, "id", entry.Id == Guid.Empty ? AgentPrismId.NewId() : entry.Id);
        DbHelpers.Add(command, "tenant_id", entry.TenantId);
        AddNullableText(command, "actor", entry.Actor);
        DbHelpers.Add(command, "action", entry.Action);
        DbHelpers.Add(command, "entity", entry.Entity);
        AddNullableJsonb(command, "before", entry.Before);
        AddNullableJsonb(command, "after", entry.After);
        Dialect.AddTimestamp(command, "created_at", entry.CreatedAt);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(
        AuditQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = CreateCommand(_sql.SelectAuditLog);
        DbHelpers.Add(command, "tenant_id", query.TenantId ?? string.Empty);
        AddNullableText(command, "actor", query.Actor);
        AddNullableText(command, "action", query.Action);
        AddNullableText(command, "entity", query.Entity);
        Dialect.AddTimestamp(command, "started_after", query.After);
        Dialect.AddTimestamp(command, "started_before", query.Before);
        DbHelpers.Add(command, "take", Math.Max(query.Limit, 0));

        return await DbHelpers.ReadListAsync(command, ReadEntry, cancellationToken).ConfigureAwait(false);
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    private static AuditEntry ReadEntry(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            Actor = DbHelpers.GetNullableString(reader, 2),
            Action = reader.GetString(3),
            Entity = reader.GetString(4),
            Before = DbHelpers.GetNullableString(reader, 5),
            After = DbHelpers.GetNullableString(reader, 6),
            CreatedAt = DbHelpers.GetTimestamp(reader, 7),
        };

    private void AddNullableText(DbCommand command, string name, string? value)
        => Dialect.AddText(command, name, value);

    private void AddNullableJsonb(DbCommand command, string name, string? value)
        => Dialect.AddJsonb(command, name, value);
}
