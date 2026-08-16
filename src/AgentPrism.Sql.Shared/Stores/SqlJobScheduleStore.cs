using System.Data.Common;
using System.Text.Json;

namespace AgentPrism;

/// <summary>Stores schedule definitions in the SQL database.</summary>
/// <remarks>
/// The behavior contract is identical to <see cref="InMemoryJobScheduleStore"/>
/// and is guarded by the shared contract tests.
/// </remarks>
internal sealed class SqlJobScheduleStore : IJobScheduleStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Creates a new schedule store.</summary>
    /// <param name="context">The store context.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public SqlJobScheduleStore(SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    /// <summary>The gateway for provider-specific behavior.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask<JobSchedule?> GetAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var command = CreateCommand(_sql.SelectJobSchedule);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "name", name);

        return await DbHelpers.ReadSingleAsync(command, ReadSchedule, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<JobSchedule>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectJobSchedules);
        DbHelpers.Add(command, "tenant_id", tenantId);

        return await DbHelpers.ReadListAsync(command, ReadSchedule, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<JobSchedule> SaveAsync(JobSchedule schedule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        var id = schedule.Id == Guid.Empty ? AgentPrismId.NewId() : schedule.Id;

        var command = CreateCommand(_sql.UpsertJobSchedule);
        DbHelpers.Add(command, "id", id);
        DbHelpers.Add(command, "tenant_id", schedule.TenantId);
        DbHelpers.Add(command, "name", schedule.Name);
        DbHelpers.Add(command, "kind", (short)schedule.Kind);
        DbHelpers.Add(command, "target_name", schedule.TargetName);
        AddNullableText(command, "cron", schedule.Cron);
        DbHelpers.Add(command, "time_zone", schedule.TimeZone);
        Dialect.AddJsonb(command, "payload", RawJson(schedule.Payload));
        DbHelpers.Add(command, "enabled", schedule.Enabled);
        AddNullableTimestamp(command, "next_run_at", schedule.NextRunAt);
        AddNullableTimestamp(command, "last_run_at", schedule.LastRunAt);
        AddNullableText(command, "created_by", schedule.CreatedBy);
        Dialect.AddTimestamp(command, "created_at", schedule.CreatedAt);
        Dialect.AddTimestamp(command, "updated_at", schedule.UpdatedAt);

        var written = await DbHelpers.ReadSingleAsync(
                command,
                static reader => new WrittenSchedule(
                    reader.GetGuid(0),
                    DbHelpers.GetNullableString(reader, 1),
                    DbHelpers.GetTimestamp(reader, 2)),
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
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "name", name);

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "The scheduler scans ALL tenants' due schedules in a single pass; the tenant travels in the generated job's record (JobRecord.TenantId), and the worker runs with that tenant.")]
    public async ValueTask<IReadOnlyList<JobSchedule>> ListDueAsync(
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectDueJobSchedules);
        Dialect.AddTimestamp(command, "as_of", asOfUtc);

        return await DbHelpers.ReadListAsync(command, ReadSchedule, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "The schedule id comes from a row returned by ListDueAsync; it is an optimistic-lock race resolution, not a tenant restriction.")]
    public async ValueTask<bool> TryClaimNextRunAsync(
        Guid scheduleId,
        DateTimeOffset expectedNextRunAt,
        DateTimeOffset newNextRunAt,
        DateTimeOffset ranAt,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.TryClaimJobScheduleNextRun);
        DbHelpers.Add(command, "id", scheduleId);
        Dialect.AddTimestamp(command, "expected_next_run_at", expectedNextRunAt);
        Dialect.AddTimestamp(command, "new_next_run_at", newNextRunAt);
        Dialect.AddTimestamp(command, "ran_at", ranAt);

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    private static JobSchedule ReadSchedule(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            Name = reader.GetString(2),
            Kind = (JobKind)reader.GetInt16(3),
            TargetName = reader.GetString(4),
            Cron = DbHelpers.GetNullableString(reader, 5),
            TimeZone = reader.GetString(6),
            Payload = ReadJsonb(reader, 7),
            Enabled = reader.GetBoolean(8),
            NextRunAt = DbHelpers.GetNullableTimestamp(reader, 9),
            LastRunAt = DbHelpers.GetNullableTimestamp(reader, 10),
            CreatedBy = DbHelpers.GetNullableString(reader, 11),
            CreatedAt = DbHelpers.GetTimestamp(reader, 12),
            UpdatedAt = DbHelpers.GetTimestamp(reader, 13),
        };

    /// <summary>
    /// Reads a jsonb column as a <see cref="JsonElement"/>.
    /// </summary>
    /// <remarks>
    /// When a <see cref="JsonDocument"/> is disposed it returns its buffer, and
    /// a <see cref="JsonElement"/> taken from it becomes invalid; <c>Clone()</c>
    /// copies the buffer and makes the value independent of the caller's lifetime.
    /// </remarks>
    private static JsonElement ReadJsonb(DbDataReader reader, int ordinal)
    {
        using var document = JsonDocument.Parse(reader.GetString(ordinal));

        return document.RootElement.Clone();
    }

    private static string RawJson(JsonElement payload)
        => payload.ValueKind == JsonValueKind.Undefined ? "null" : payload.GetRawText();

    private void AddNullableText(DbCommand command, string name, string? value)
        => Dialect.AddText(command, name, value);

    private void AddNullableTimestamp(DbCommand command, string name, DateTimeOffset? value)
        => Dialect.AddTimestamp(command, name, value);

    private sealed record WrittenSchedule(Guid Id, string? CreatedBy, DateTimeOffset CreatedAt);
}
