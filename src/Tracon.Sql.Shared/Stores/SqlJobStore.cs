using System.Data.Common;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Stores queued jobs and their items in the SQL database.</summary>
/// <remarks>
/// <para>
/// <see cref="LeaseAsync"/> uses <c>FOR UPDATE SKIP LOCKED</c>: even when
/// multiple workers connect to the same database, a job is picked up by only
/// one worker. The behavior contract is identical to <c>InMemoryJobStore</c>
/// and is protected by shared contract tests (including concurrency).
/// </para>
/// </remarks>
internal sealed class SqlJobStore : IJobStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;
    private readonly IOptionsMonitor<TraconSchedulingOptions>? _schedulingOptions;

    /// <summary>Creates a new job store.</summary>
    /// <param name="context">The store context.</param>
    /// <param name="schedulingOptions">
    /// The source for <c>LaneByHandlerKey</c>. <see langword="null"/> disables lane-by-key resolution.
    /// </param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public SqlJobStore(SqlStoreContext context, IOptionsMonitor<TraconSchedulingOptions>? schedulingOptions = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
        _schedulingOptions = schedulingOptions;
    }

    /// <summary>The gateway for provider-specific behavior.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask<JobRecord> EnqueueAsync(
        JobRecord job,
        IReadOnlyList<string> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(items);

        var lane = JobLanes.Resolve(job.Lane, job.HandlerKey, _schedulingOptions?.CurrentValue.LaneByHandlerKey);

        var connection = await _context.DataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                var insertJob = _context.CreateCommand(_sql.InsertJob, connection, transaction);
                DbHelpers.Add(insertJob, "id", job.Id);
                DbHelpers.Add(insertJob, "tenant_id", job.TenantId);
                AddNullableUuid(insertJob, "schedule_id", job.ScheduleId);
                DbHelpers.Add(insertJob, "handler_key", job.HandlerKey);
                DbHelpers.Add(insertJob, "target_name", job.TargetName);
                Dialect.AddJsonb(insertJob, "payload", RawJson(job.Payload));
                DbHelpers.Add(insertJob, "total_items", items.Count);
                Dialect.AddTimestamp(insertJob, "scheduled_for", job.ScheduledFor);
                Dialect.AddTimestamp(insertJob, "created_at", job.CreatedAt);
                Dialect.AddInt16(insertJob, "max_attempts", job.MaxAttempts is { } maxAttempts ? (short)maxAttempts : null);
                DbHelpers.Add(insertJob, "lane", lane);
                await DbHelpers.ExecuteAsync(insertJob, cancellationToken).ConfigureAwait(false);

                if (items.Count > 0)
                {
                    var ids = new Guid[items.Count];

                    for (var index = 0; index < items.Count; index++)
                    {
                        ids[index] = Guid.NewGuid();
                    }

                    var insertItems = _context.CreateCommand(_sql.InsertJobItems, connection, transaction);
                    DbHelpers.Add(insertItems, "job_id", job.Id);
                    Dialect.AddUuidArray(insertItems, "ids", ids);
                    Dialect.AddTextArray(insertItems, "inputs", items);
                    await DbHelpers.ExecuteAsync(insertItems, cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        return job with
        {
            Lane = lane,
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
    [TenantAgnostic(
        "The worker pool picks up jobs in order from EVERY tenant's queue; the tenant is carried on the leased job's record, and execution happens within that tenant's scope (AmbientTenantScope).")]
    public async ValueTask<JobRecord?> LeaseAsync(
        string owner,
        TimeSpan leaseDuration,
        IReadOnlyList<string>? lanes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        var now = DateTimeOffset.UtcNow;

        var command = CreateCommand(_sql.LeaseJob);
        DbHelpers.Add(command, "owner", owner);
        Dialect.AddTimestamp(command, "lease_until", (now + leaseDuration));
        Dialect.AddTimestamp(command, "now", now);
        Dialect.AddTextArray(command, "lanes", lanes is { Count: > 0 } ? lanes : null);

        return await DbHelpers.ReadSingleAsync(command, ReadJob, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Lease renewal is guarded by the job's owning WORKER identity (owner); it carries no tenant concept.")]
    public async ValueTask RenewLeaseAsync(
        Guid jobId,
        string owner,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        var command = CreateCommand(_sql.RenewJobLease);
        DbHelpers.Add(command, "id", jobId);
        DbHelpers.Add(command, "owner", owner);
        Dialect.AddTimestamp(command, "lease_until", (DateTimeOffset.UtcNow + leaseDuration));

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Same rationale as RenewLeaseAsync: ownership is guarded by the worker identity.")]
    public async ValueTask<bool> MarkRunningAsync(Guid jobId, string owner, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        var command = CreateCommand(_sql.MarkJobRunning);
        DbHelpers.Add(command, "id", jobId);
        DbHelpers.Add(command, "owner", owner);

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "The job id comes from LeaseAsync; the call carries no separate tenant intent.")]
    public async ValueTask CompleteAsync(JobCompletion completion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);

        var command = CreateCommand(_sql.CompleteJob);
        DbHelpers.Add(command, "id", completion.JobId);
        DbHelpers.Add(command, "status", (short)completion.Status);
        Dialect.AddTimestamp(command, "completed_at", completion.CompletedAt);
        AddNullableText(command, "error_message", completion.ErrorMessage);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Same rationale as CompleteAsync: the job id comes from LeaseAsync.")]
    public async ValueTask ReleaseForRetryAsync(
        Guid jobId,
        string errorMessage,
        TimeSpan? retryAfter = null,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.ReleaseJobForRetry);
        DbHelpers.Add(command, "id", jobId);
        AddNullableText(command, "error_message", errorMessage);

        // Backoff is set up via scheduled_for; LeaseJob already filters
        // `scheduled_for <= @now`. GREATEST(...) never brings the wait time
        // forward: an early-running round cannot pull the job back.
        var retryAt = retryAfter is { } delay && delay > TimeSpan.Zero
            ? DateTimeOffset.UtcNow + delay
            : (DateTimeOffset?)null;

        Dialect.AddTimestamp(command, "retry_at", retryAt);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> CancelAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.CancelJob);
        DbHelpers.Add(command, "id", jobId);
        DbHelpers.Add(command, "tenant_id", tenantId);
        Dialect.AddTimestamp(command, "completed_at", DateTimeOffset.UtcNow);

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    public async ValueTask<JobRecord?> GetAsync(
        string tenantId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectJob);
        DbHelpers.Add(command, "id", jobId);
        DbHelpers.Add(command, "tenant_id", tenantId);

        return await DbHelpers.ReadSingleAsync(command, ReadJob, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<JobRecord>> QueryAsync(
        JobQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = CreateCommand(_sql.SelectJobs);
        AddNullableText(command, "tenant_id", query.TenantId);
        AddNullableText(command, "handler_key", query.HandlerKey);
        Dialect.AddInt16(command, "status", (short?)query.Status);
        AddNullableUuid(command, "schedule_id", query.ScheduleId);
        AddNullableText(command, "lane", query.Lane);
        DbHelpers.Add(command, "skip", Math.Max(query.Skip, 0));
        DbHelpers.Add(command, "take", Math.Max(query.Take, 0));

        return await DbHelpers.ReadListAsync(command, ReadJob, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Items live UNDER a job; the job id comes from a query already filtered by tenant (GetAsync/QueryAsync).")]
    public async ValueTask<IReadOnlyList<JobItemRecord>> ListItemsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectJobItems);
        DbHelpers.Add(command, "job_id", jobId);

        return await DbHelpers.ReadListAsync(command, ReadJobItem, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "The item result is written by the worker running the job; the job id comes from LeaseAsync.")]
    public async ValueTask ReportItemAsync(JobItemResult item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var command = CreateCommand(_sql.ReportJobItem);
        DbHelpers.Add(command, "job_id", item.JobId);
        DbHelpers.Add(command, "seq", item.Seq);
        DbHelpers.Add(command, "status", (short)item.Status);
        AddNullableUuid(command, "run_id", item.RunId);
        AddNullableText(command, "error", item.Error);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Queue depth is an operator signal about the worker pool, which leases across EVERY tenant (LeaseAsync); there is no tenant boundary to apply and no tenant tag is published.")]
    public async ValueTask<IReadOnlyList<JobQueueDepth>> GetQueueDepthAsync(CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectJobQueueDepth);

        return await DbHelpers.ReadListAsync(command, ReadQueueDepth, cancellationToken).ConfigureAwait(false);
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    private static JobQueueDepth ReadQueueDepth(DbDataReader reader)
        => new()
        {
            Lane = reader.GetString(0),
            Status = (JobStatus)reader.GetInt16(1),
            Count = reader.GetInt64(2),
        };

    private static JobRecord ReadJob(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            ScheduleId = reader.IsDBNull(2) ? null : reader.GetGuid(2),
            HandlerKey = reader.GetString(3),
            TargetName = reader.GetString(4),
            Status = (JobStatus)reader.GetInt16(5),
            Payload = ReadJsonb(reader, 6),
            TotalItems = reader.GetInt32(7),
            DoneItems = reader.GetInt32(8),
            FailedItems = reader.GetInt32(9),
            Attempt = reader.GetInt16(10),
            LeaseOwner = DbHelpers.GetNullableString(reader, 11),
            LeaseUntil = DbHelpers.GetNullableTimestamp(reader, 12),
            ScheduledFor = DbHelpers.GetTimestamp(reader, 13),
            StartedAt = DbHelpers.GetNullableTimestamp(reader, 14),
            CompletedAt = DbHelpers.GetNullableTimestamp(reader, 15),
            ErrorMessage = DbHelpers.GetNullableString(reader, 16),
            CreatedAt = DbHelpers.GetTimestamp(reader, 17),
            MaxAttempts = reader.IsDBNull(18) ? null : reader.GetInt16(18),
            Lane = reader.GetString(19),
        };

    private static JobItemRecord ReadJobItem(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            JobId = reader.GetGuid(1),
            Seq = reader.GetInt32(2),
            Input = reader.GetString(3),
            RunId = reader.IsDBNull(4) ? null : reader.GetGuid(4),
            Status = (JobItemStatus)reader.GetInt16(5),
            Error = DbHelpers.GetNullableString(reader, 6),
        };

    /// <summary>
    /// Reads a jsonb column as a <see cref="JsonElement"/>.
    /// </summary>
    /// <remarks>
    /// Disposing a <see cref="JsonDocument"/> returns its buffer, invalidating
    /// any <see cref="JsonElement"/> taken from it; <c>Clone()</c> copies the
    /// buffer and makes the value independent of the caller's lifetime.
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

    private void AddNullableUuid(DbCommand command, string name, Guid? value)
        => Dialect.AddUuid(command, name, value);
}
