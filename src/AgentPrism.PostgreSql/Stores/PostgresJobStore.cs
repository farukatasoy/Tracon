using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace AgentPrism;

/// <summary>Kuyruktaki isleri ve ogelerini PostgreSQL'de saklayan depo.</summary>
/// <remarks>
/// <para>
/// <see cref="LeaseAsync"/> <c>FOR UPDATE SKIP LOCKED</c> kullanir: birden
/// fazla isci ayni veritabanina baglansa bile bir is yalnizca bir isci
/// tarafindan alinir. Davranis sozlesmesi <see cref="InMemoryJobStore"/> ile
/// birebir aynidir ve ortak sozlesme testleriyle (eslerlik dahil) korunur.
/// </para>
/// </remarks>
public sealed class PostgresJobStore : IJobStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly SqlQueries _sql;
    private readonly int _commandTimeout;

    /// <summary>Yeni bir is deposu olusturur.</summary>
    /// <param name="dataSource">Veri kaynagi.</param>
    /// <param name="options">PostgreSQL ayarlari.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public PostgresJobStore(NpgsqlDataSource dataSource, IOptions<AgentPrismPostgreSqlOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);

        _dataSource = dataSource;
        _sql = new SqlQueries(options.Value.SchemaName);
        _commandTimeout = options.Value.CommandTimeoutSeconds;
    }

    /// <inheritdoc />
    public async ValueTask<JobRecord> EnqueueAsync(
        JobRecord job,
        IReadOnlyList<string> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(items);

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                var insertJob = new NpgsqlCommand(_sql.InsertJob, connection, transaction)
                {
                    CommandTimeout = _commandTimeout,
                };
                insertJob.Parameters.AddWithValue("id", job.Id);
                insertJob.Parameters.AddWithValue("tenant_id", job.TenantId);
                AddNullableUuid(insertJob, "schedule_id", job.ScheduleId);
                insertJob.Parameters.AddWithValue("kind", (short)job.Kind);
                insertJob.Parameters.AddWithValue("target_name", job.TargetName);
                insertJob.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = RawJson(job.Payload) });
                insertJob.Parameters.AddWithValue("total_items", items.Count);
                insertJob.Parameters.AddWithValue("scheduled_for", job.ScheduledFor.UtcDateTime);
                insertJob.Parameters.AddWithValue("created_at", job.CreatedAt.UtcDateTime);
                await NpgsqlHelpers.ExecuteAsync(insertJob, cancellationToken).ConfigureAwait(false);

                if (items.Count > 0)
                {
                    var ids = new Guid[items.Count];

                    for (var index = 0; index < items.Count; index++)
                    {
                        ids[index] = Guid.NewGuid();
                    }

                    var insertItems = new NpgsqlCommand(_sql.InsertJobItems, connection, transaction)
                    {
                        CommandTimeout = _commandTimeout,
                    };
                    insertItems.Parameters.AddWithValue("job_id", job.Id);
                    insertItems.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
                    {
                        Value = ids,
                    });
                    insertItems.Parameters.Add(new NpgsqlParameter("inputs", NpgsqlDbType.Array | NpgsqlDbType.Text)
                    {
                        Value = items.ToArray(),
                    });
                    await NpgsqlHelpers.ExecuteAsync(insertItems, cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        return job with
        {
            Status = JobStatus.Pending,
            TotalItems = items.Count,
            DoneItems = 0,
            FailedItems = 0,
            Attempt = 0,
            LeaseOwner = null,
            LeaseUntil = null,
            StartedAt = null,
            CompletedAt = null,
            ErrorMessage = null,
        };
    }

    /// <inheritdoc />
    public async ValueTask<JobRecord?> LeaseAsync(
        string owner,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        var now = DateTimeOffset.UtcNow;

        var command = CreateCommand(_sql.LeaseJob);
        command.Parameters.AddWithValue("owner", owner);
        command.Parameters.AddWithValue("lease_until", (now + leaseDuration).UtcDateTime);
        command.Parameters.AddWithValue("now", now.UtcDateTime);

        return await NpgsqlHelpers.ReadSingleAsync(command, ReadJob, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask RenewLeaseAsync(
        Guid jobId,
        string owner,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        var command = CreateCommand(_sql.RenewJobLease);
        command.Parameters.AddWithValue("id", jobId);
        command.Parameters.AddWithValue("owner", owner);
        command.Parameters.AddWithValue("lease_until", (DateTimeOffset.UtcNow + leaseDuration).UtcDateTime);

        await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> MarkRunningAsync(Guid jobId, string owner, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        var command = CreateCommand(_sql.MarkJobRunning);
        command.Parameters.AddWithValue("id", jobId);
        command.Parameters.AddWithValue("owner", owner);

        return await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    public async ValueTask CompleteAsync(JobCompletion completion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);

        var command = CreateCommand(_sql.CompleteJob);
        command.Parameters.AddWithValue("id", completion.JobId);
        command.Parameters.AddWithValue("status", (short)completion.Status);
        command.Parameters.AddWithValue("completed_at", completion.CompletedAt.UtcDateTime);
        AddNullableText(command, "error_message", completion.ErrorMessage);

        await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask ReleaseForRetryAsync(
        Guid jobId,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.ReleaseJobForRetry);
        command.Parameters.AddWithValue("id", jobId);
        AddNullableText(command, "error_message", errorMessage);

        await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> CancelAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.CancelJob);
        command.Parameters.AddWithValue("id", jobId);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("completed_at", DateTimeOffset.UtcNow.UtcDateTime);

        return await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    public async ValueTask<JobRecord?> GetAsync(
        string tenantId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectJob);
        command.Parameters.AddWithValue("id", jobId);
        command.Parameters.AddWithValue("tenant_id", tenantId);

        return await NpgsqlHelpers.ReadSingleAsync(command, ReadJob, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<JobRecord>> QueryAsync(
        JobQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = CreateCommand(_sql.SelectJobs);
        AddNullableText(command, "tenant_id", query.TenantId);
        command.Parameters.Add(new NpgsqlParameter("kind", NpgsqlDbType.Smallint)
        {
            Value = query.Kind is { } kind ? (object)(short)kind : DBNull.Value,
        });
        command.Parameters.Add(new NpgsqlParameter("status", NpgsqlDbType.Smallint)
        {
            Value = query.Status is { } status ? (object)(short)status : DBNull.Value,
        });
        AddNullableUuid(command, "schedule_id", query.ScheduleId);
        command.Parameters.AddWithValue("skip", Math.Max(query.Skip, 0));
        command.Parameters.AddWithValue("take", Math.Max(query.Take, 0));

        return await NpgsqlHelpers.ReadListAsync(command, ReadJob, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<JobItemRecord>> ListItemsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectJobItems);
        command.Parameters.AddWithValue("job_id", jobId);

        return await NpgsqlHelpers.ReadListAsync(command, ReadJobItem, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask ReportItemAsync(JobItemResult item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var command = CreateCommand(_sql.ReportJobItem);
        command.Parameters.AddWithValue("job_id", item.JobId);
        command.Parameters.AddWithValue("seq", item.Seq);
        command.Parameters.AddWithValue("status", (short)item.Status);
        AddNullableUuid(command, "run_id", item.RunId);
        AddNullableText(command, "error", item.Error);

        await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private NpgsqlCommand CreateCommand(string sql)
    {
        var command = _dataSource.CreateCommand(sql);
        command.CommandTimeout = _commandTimeout;

        return command;
    }

    private static JobRecord ReadJob(NpgsqlDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            ScheduleId = reader.IsDBNull(2) ? null : reader.GetGuid(2),
            Kind = (JobKind)reader.GetInt16(3),
            TargetName = reader.GetString(4),
            Status = (JobStatus)reader.GetInt16(5),
            Payload = ReadJsonb(reader, 6),
            TotalItems = reader.GetInt32(7),
            DoneItems = reader.GetInt32(8),
            FailedItems = reader.GetInt32(9),
            Attempt = reader.GetInt16(10),
            LeaseOwner = NpgsqlHelpers.GetNullableString(reader, 11),
            LeaseUntil = NpgsqlHelpers.GetNullableTimestamp(reader, 12),
            ScheduledFor = NpgsqlHelpers.GetTimestamp(reader, 13),
            StartedAt = NpgsqlHelpers.GetNullableTimestamp(reader, 14),
            CompletedAt = NpgsqlHelpers.GetNullableTimestamp(reader, 15),
            ErrorMessage = NpgsqlHelpers.GetNullableString(reader, 16),
            CreatedAt = NpgsqlHelpers.GetTimestamp(reader, 17),
        };

    private static JobItemRecord ReadJobItem(NpgsqlDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            JobId = reader.GetGuid(1),
            Seq = reader.GetInt32(2),
            Input = reader.GetString(3),
            RunId = reader.IsDBNull(4) ? null : reader.GetGuid(4),
            Status = (JobItemStatus)reader.GetInt16(5),
            Error = NpgsqlHelpers.GetNullableString(reader, 6),
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

    private static void AddNullableUuid(NpgsqlCommand command, string name, Guid? value)
        => command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Uuid)
        {
            Value = value.HasValue ? (object)value.Value : DBNull.Value,
        });
}
