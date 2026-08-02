using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace AgentPrism;

/// <summary>Denetim izi kayitlarini PostgreSQL'de saklayan defter.</summary>
/// <remarks>
/// Yazma <c>tenant_id, created_at</c> indeksinden gecen bir okuma ile eslenir
/// (0001'de kurulan <c>audit_log_tenant_created_idx</c>). Migration gerekmez;
/// sema Faz 0'dan beri yeterlidir.
/// </remarks>
public sealed class PostgresAuditLog : IAuditLog
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly SqlQueries _sql;
    private readonly int _commandTimeout;

    /// <summary>Yeni bir denetim izi defteri olusturur.</summary>
    /// <param name="dataSource">Veri kaynagi.</param>
    /// <param name="options">PostgreSQL ayarlari.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public PostgresAuditLog(NpgsqlDataSource dataSource, IOptions<AgentPrismPostgreSqlOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);

        _dataSource = dataSource;
        _sql = new SqlQueries(options.Value.SchemaName);
        _commandTimeout = options.Value.CommandTimeoutSeconds;
    }

    /// <inheritdoc />
    public async ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var command = CreateCommand(_sql.InsertAuditEntry);
        command.Parameters.AddWithValue("id", entry.Id == Guid.Empty ? AgentPrismId.NewId() : entry.Id);
        command.Parameters.AddWithValue("tenant_id", entry.TenantId);
        AddNullableText(command, "actor", entry.Actor);
        command.Parameters.AddWithValue("action", entry.Action);
        command.Parameters.AddWithValue("entity", entry.Entity);
        AddNullableJsonb(command, "before", entry.Before);
        AddNullableJsonb(command, "after", entry.After);
        command.Parameters.AddWithValue("created_at", entry.CreatedAt.UtcDateTime);

        await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(
        AuditQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = CreateCommand(_sql.SelectAuditLog);
        command.Parameters.AddWithValue("tenant_id", query.TenantId ?? string.Empty);
        AddNullableText(command, "actor", query.Actor);
        AddNullableText(command, "action", query.Action);
        AddNullableText(command, "entity", query.Entity);
        command.Parameters.Add(new NpgsqlParameter("started_after", NpgsqlDbType.TimestampTz)
        {
            Value = query.After is { } after ? (object)after.UtcDateTime : DBNull.Value,
        });
        command.Parameters.Add(new NpgsqlParameter("started_before", NpgsqlDbType.TimestampTz)
        {
            Value = query.Before is { } before ? (object)before.UtcDateTime : DBNull.Value,
        });
        command.Parameters.AddWithValue("take", Math.Max(query.Limit, 0));

        return await NpgsqlHelpers.ReadListAsync(command, ReadEntry, cancellationToken).ConfigureAwait(false);
    }

    private NpgsqlCommand CreateCommand(string sql)
    {
        var command = _dataSource.CreateCommand(sql);
        command.CommandTimeout = _commandTimeout;

        return command;
    }

    private static AuditEntry ReadEntry(NpgsqlDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            Actor = NpgsqlHelpers.GetNullableString(reader, 2),
            Action = reader.GetString(3),
            Entity = reader.GetString(4),
            Before = NpgsqlHelpers.GetNullableString(reader, 5),
            After = NpgsqlHelpers.GetNullableString(reader, 6),
            CreatedAt = NpgsqlHelpers.GetTimestamp(reader, 7),
        };

    private static void AddNullableText(NpgsqlCommand command, string name, string? value)
        => command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Text)
        {
            Value = (object?)value ?? DBNull.Value,
        });

    private static void AddNullableJsonb(NpgsqlCommand command, string name, string? value)
        => command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb)
        {
            Value = (object?)value ?? DBNull.Value,
        });
}
