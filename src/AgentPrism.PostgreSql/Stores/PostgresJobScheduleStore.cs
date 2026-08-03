using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace AgentPrism;

/// <summary>Zamanlama tanimlarini PostgreSQL'de saklayan depo.</summary>
/// <remarks>
/// Davranis sozlesmesi <see cref="InMemoryJobScheduleStore"/> ile birebir
/// aynidir ve ortak sozlesme testleriyle korunur.
/// </remarks>
public sealed class PostgresJobScheduleStore : IJobScheduleStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly SqlQueries _sql;
    private readonly int _commandTimeout;

    /// <summary>Yeni bir zamanlama deposu olusturur.</summary>
    /// <param name="dataSource">Veri kaynagi.</param>
    /// <param name="options">PostgreSQL ayarlari.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public PostgresJobScheduleStore(NpgsqlDataSource dataSource, IOptions<AgentPrismPostgreSqlOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);

        _dataSource = dataSource;
        _sql = new SqlQueries(options.Value.SchemaName);
        _commandTimeout = options.Value.CommandTimeoutSeconds;
    }

    /// <inheritdoc />
    public async ValueTask<JobSchedule?> GetAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var command = CreateCommand(_sql.SelectJobSchedule);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("name", name);

        return await NpgsqlHelpers.ReadSingleAsync(command, ReadSchedule, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<JobSchedule>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectJobSchedules);
        command.Parameters.AddWithValue("tenant_id", tenantId);

        return await NpgsqlHelpers.ReadListAsync(command, ReadSchedule, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<JobSchedule> SaveAsync(JobSchedule schedule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        var id = schedule.Id == Guid.Empty ? AgentPrismId.NewId() : schedule.Id;

        var command = CreateCommand(_sql.UpsertJobSchedule);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("tenant_id", schedule.TenantId);
        command.Parameters.AddWithValue("name", schedule.Name);
        command.Parameters.AddWithValue("kind", (short)schedule.Kind);
        command.Parameters.AddWithValue("target_name", schedule.TargetName);
        AddNullableText(command, "cron", schedule.Cron);
        command.Parameters.AddWithValue("time_zone", schedule.TimeZone);
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = RawJson(schedule.Payload) });
        command.Parameters.AddWithValue("enabled", schedule.Enabled);
        AddNullableTimestamp(command, "next_run_at", schedule.NextRunAt);
        AddNullableTimestamp(command, "last_run_at", schedule.LastRunAt);
        AddNullableText(command, "created_by", schedule.CreatedBy);
        command.Parameters.AddWithValue("created_at", schedule.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", schedule.UpdatedAt.UtcDateTime);

        var written = await NpgsqlHelpers.ReadSingleAsync(
                command,
                static reader => new WrittenSchedule(
                    reader.GetGuid(0),
                    NpgsqlHelpers.GetNullableString(reader, 1),
                    NpgsqlHelpers.GetTimestamp(reader, 2)),
                cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismException($"'{schedule.Name}' zamanlamasi kaydedilemedi.");

        return schedule with { Id = written.Id, CreatedBy = written.CreatedBy, CreatedAt = written.CreatedAt };
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var command = CreateCommand(_sql.DeleteJobSchedule);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("name", name);

        return await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<JobSchedule>> ListDueAsync(
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectDueJobSchedules);
        command.Parameters.AddWithValue("as_of", asOfUtc.UtcDateTime);

        return await NpgsqlHelpers.ReadListAsync(command, ReadSchedule, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryClaimNextRunAsync(
        Guid scheduleId,
        DateTimeOffset expectedNextRunAt,
        DateTimeOffset newNextRunAt,
        DateTimeOffset ranAt,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.TryClaimJobScheduleNextRun);
        command.Parameters.AddWithValue("id", scheduleId);
        command.Parameters.AddWithValue("expected_next_run_at", expectedNextRunAt.UtcDateTime);
        command.Parameters.AddWithValue("new_next_run_at", newNextRunAt.UtcDateTime);
        command.Parameters.AddWithValue("ran_at", ranAt.UtcDateTime);

        return await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    private NpgsqlCommand CreateCommand(string sql)
    {
        var command = _dataSource.CreateCommand(sql);
        command.CommandTimeout = _commandTimeout;

        return command;
    }

    private static JobSchedule ReadSchedule(NpgsqlDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            Name = reader.GetString(2),
            Kind = (JobKind)reader.GetInt16(3),
            TargetName = reader.GetString(4),
            Cron = NpgsqlHelpers.GetNullableString(reader, 5),
            TimeZone = reader.GetString(6),
            Payload = ReadJsonb(reader, 7),
            Enabled = reader.GetBoolean(8),
            NextRunAt = NpgsqlHelpers.GetNullableTimestamp(reader, 9),
            LastRunAt = NpgsqlHelpers.GetNullableTimestamp(reader, 10),
            CreatedBy = NpgsqlHelpers.GetNullableString(reader, 11),
            CreatedAt = NpgsqlHelpers.GetTimestamp(reader, 12),
            UpdatedAt = NpgsqlHelpers.GetTimestamp(reader, 13),
        };

    /// <summary>
    /// jsonb sutununu <see cref="JsonElement"/> olarak okur.
    /// </summary>
    /// <remarks>
    /// <see cref="JsonDocument"/> birakildiginda kendi tamponunu geri verir ve
    /// icinden alinan <see cref="JsonElement"/> gecersizlesir; <c>Clone()</c>
    /// tamponu kopyalar ve degeri cagiranin omrunden bagimsiz kilar.
    /// </remarks>
    private static JsonElement ReadJsonb(NpgsqlDataReader reader, int ordinal)
    {
        using var document = JsonDocument.Parse(reader.GetString(ordinal));

        return document.RootElement.Clone();
    }

    private static string RawJson(JsonElement payload)
        => payload.ValueKind == JsonValueKind.Undefined ? "null" : payload.GetRawText();

    private static void AddNullableText(NpgsqlCommand command, string name, string? value)
        => command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Text)
        {
            Value = (object?)value ?? DBNull.Value,
        });

    private static void AddNullableTimestamp(NpgsqlCommand command, string name, DateTimeOffset? value)
        => command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.TimestampTz)
        {
            Value = value is { } dateTimeOffset ? (object)dateTimeOffset.UtcDateTime : DBNull.Value,
        });

    private sealed record WrittenSchedule(Guid Id, string? CreatedBy, DateTimeOffset CreatedAt);
}
