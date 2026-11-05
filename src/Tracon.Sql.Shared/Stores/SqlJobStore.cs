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
/// <para>
/// On PostgreSQL and SQL Server a lease, a retry delay and the "is this job due" test all read the database server's
/// clock inside the statement, so every node that shares the database decides from one clock and a node whose own clock
/// is wrong cannot take a job another node is running. SQLite is used by one process and keeps the application clock.
/// </para>
/// </remarks>
internal sealed class SqlJobStore : IJobStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;
    private readonly IOptionsMonitor<TraconSchedulingOptions>? _schedulingOptions;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a new job store.</summary>
    /// <param name="context">The store context.</param>
    /// <param name="schedulingOptions">
    /// The source for <c>LaneByHandlerKey</c>. <see langword="null"/> disables lane-by-key resolution.
    /// </param>
    /// <param name="timeProvider">
    /// The application clock. Lease times and retry delays use it on a provider whose statements take the time as a
    /// parameter (SQLite) and ignore it on PostgreSQL and SQL Server, which read the database server's clock; the
    /// cancellation time is always stamped with it. Defaults to <see cref="TimeProvider.System"/> when not given.
    /// </param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public SqlJobStore(
        SqlStoreContext context,
        IOptionsMonitor<TraconSchedulingOptions>? schedulingOptions = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
        _schedulingOptions = schedulingOptions;
        _timeProvider = timeProvider ?? TimeProvider.System;
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

        // Folded here, not only inside AddTenant: the read-back below looks the row up by it.
        var tenantId = AmbientTenantScope.Normalize(job.TenantId);

        // The record that goes back to the caller is the row, read in the transaction that wrote it: the text, the times
        // and the payload as the store holds them (U+0000 is stored as U+FFFD, PostgreSQL keeps microseconds, a JSON
        // column does not keep the caller's spacing), the same record a GetAsync an instant later reports.
        return await _context.ExecuteInTransactionAsync(
            async (connection, transaction) =>
            {
                var insertJob = _context.CreateCommand(_sql.InsertJob, connection, transaction);
                DbHelpers.Add(insertJob, "id", job.Id);
                DbHelpers.AddTenant(insertJob, tenantId);
                AddNullableUuid(insertJob, "schedule_id", job.ScheduleId);
                DbHelpers.Add(insertJob, "handler_key", job.HandlerKey);
                DbHelpers.Add(insertJob, "target_name", job.TargetName);
                Dialect.AddJsonb(insertJob, "payload", RawJson(job.Payload));
                DbHelpers.Add(insertJob, "total_items", items.Count);
                Dialect.AddTimestamp(insertJob, "scheduled_for", job.ScheduledFor);
                Dialect.AddTimestamp(insertJob, "created_at", job.CreatedAt);
                Dialect.AddInt16(insertJob, "max_attempts", job.MaxAttempts is { } maxAttempts ? (short)maxAttempts : null);

                // Whole milliseconds, like every other duration column; bigint because
                // the timer's range (about 49.7 days) is past an int of milliseconds.
                Dialect.AddInt64(insertJob, "max_duration_ms", job.MaxDuration is { } maxDuration ? (long)maxDuration.TotalMilliseconds : null);
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

                var select = _context.CreateCommand(_sql.SelectJob, connection, transaction);
                DbHelpers.Add(select, "id", job.Id);
                DbHelpers.AddTenant(select, tenantId);

                var stored = await DbHelpers.ReadSingleAsync(select, ReadJob, cancellationToken).ConfigureAwait(false)
                    ?? throw new TraconException($"The job {job.Id} could not be read back after it was queued.");

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                return stored;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "The worker pool picks up jobs in order from EVERY tenant's queue; the tenant is carried on the leased job's record, and execution happens within that tenant's scope (AmbientTenantScope).")]
    public async ValueTask<JobRecord?> LeaseAsync(
        JobLeaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Owner, nameof(request));
        LeaseTimeRange.ThrowIfInvalid(request.LeaseDuration, LeaseTimeRange.LeaseRequestDurationName);

        var owner = request.Owner;

        var cap = request.TenantCap;

        if (cap is not null && (cap.HandlerKeys is not { Count: > 0 } || cap.MaxLeasedPerTenant < 1))
        {
            throw new ArgumentException(
                "A tenant cap names at least one handler key and allows at least one leased job per tenant.",
                nameof(request));
        }

        // Without a cap the statement is the one that has always run; the capped statement is a
        // separate text, so the common path pays nothing for the count.

        return await _context.ExecuteStatementAsync(
            cap is null ? _sql.LeaseJob : _sql.LeaseJobWithTenantCap,
            command =>
            {
                DbHelpers.Add(command, "owner", owner);
                Dialect.AddLeaseWindow(command, _timeProvider, request.LeaseDuration);
                Dialect.AddTextArray(command, "lanes", request.Lanes is { Count: > 0 } ? request.Lanes : null);
                if (cap is not null)
                {
                    Dialect.AddTextArray(command, "cap_keys", cap.HandlerKeys);
                    DbHelpers.Add(command, "cap_max", cap.MaxLeasedPerTenant);
                }
            },
            (command, token) => DbHelpers.ReadSingleAsync(command, ReadJob, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "A lease belongs to the job itself: the job id, the owning WORKER identity and the attempt name it. It carries no tenant concept.")]
    public async ValueTask<bool> RenewLeaseAsync(
        JobLease lease,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        LeaseTimeRange.ThrowIfInvalid(leaseDuration, nameof(leaseDuration));

        return await _context.ExecuteStatementAsync(
            _sql.RenewJobLease,
            command =>
            {
                AddLease(command, lease);
                Dialect.AddLeaseWindow(command, _timeProvider, leaseDuration);
            },
            DbHelpers.ExecuteAsync,
            cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Same rationale as RenewLeaseAsync: the lease belongs to the job itself.")]
    public async ValueTask<bool> MarkRunningAsync(JobLease lease, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);

        return await _context.ExecuteStatementAsync(
            _sql.MarkJobRunning,
            command =>
            {
                AddLease(command, lease);
            },
            DbHelpers.ExecuteAsync,
            cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Same rationale as RenewLeaseAsync: the lease belongs to the job itself.")]
    public async ValueTask<bool> CompleteAsync(JobLease lease, JobCompletion completion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(completion);

        return await _context.ExecuteStatementAsync(
            _sql.CompleteJob,
            command =>
            {
                AddLease(command, lease);
                DbHelpers.Add(command, "status", (short)completion.Status);
                Dialect.AddTimestamp(command, "completed_at", completion.CompletedAt);
                AddNullableText(command, "error_message", completion.ErrorMessage);
            },
            DbHelpers.ExecuteAsync,
            cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Same rationale as RenewLeaseAsync: the lease belongs to the job itself.")]
    public async ValueTask<bool> ReleaseForRetryAsync(
        JobLease lease,
        string errorMessage,
        TimeSpan? retryAfter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        LeaseTimeRange.ThrowIfInvalidRetryDelay(retryAfter, nameof(retryAfter));

        return await _context.ExecuteStatementAsync(
            _sql.ReleaseJobForRetry,
            command =>
            {
                AddLease(command, lease);
                AddNullableText(command, "error_message", errorMessage);
                // Backoff is set up via scheduled_for; LeaseJob already filters on it against the lease clock, and the delay
                // is measured from that same clock, so the node that releases the job and the node that leases it again
                // cannot disagree about when the delay is over. No delay leaves scheduled_for as it is.
                Dialect.AddLeaseInstant(
                    command,
                    "retry_at",
                    _timeProvider,
                    retryAfter is { } delay && delay > TimeSpan.Zero ? delay : null);
            },
            DbHelpers.ExecuteAsync,
            cancellationToken).ConfigureAwait(false) > 0;
    }

    private static void AddLease(DbCommand command, JobLease lease)
    {
        DbHelpers.Add(command, "id", lease.JobId);
        DbHelpers.Add(command, "owner", lease.Owner);
        DbHelpers.Add(command, "attempt", lease.Attempt);
    }

    /// <inheritdoc />
    public async ValueTask<bool> CancelAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        return await _context.ExecuteStatementAsync(
            _sql.CancelJob,
            command =>
            {
                DbHelpers.Add(command, "id", jobId);
                DbHelpers.AddTenant(command, tenantId);
                Dialect.AddTimestamp(command, "completed_at", _timeProvider.GetUtcNow());
            },
            DbHelpers.ExecuteAsync,
            cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    public async ValueTask<JobRecord?> GetAsync(
        string tenantId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        return await _context.ExecuteStatementAsync(
            _sql.SelectJob,
            command =>
            {
                DbHelpers.Add(command, "id", jobId);
                DbHelpers.AddTenant(command, tenantId);
            },
            (command, token) => DbHelpers.ReadSingleAsync(command, ReadJob, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<JobRecord>> QueryAsync(
        JobQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await _context.ExecuteStatementAsync(
            _sql.SelectJobs,
            command =>
            {
                AddNullableText(command, "tenant_id", AmbientTenantScope.NormalizeOrNull(query.TenantId));
                AddNullableText(command, "handler_key", query.HandlerKey);
                Dialect.AddInt16(command, "status", (short?)query.Status);
                AddNullableUuid(command, "schedule_id", query.ScheduleId);
                AddNullableText(command, "lane", query.Lane);
                DbHelpers.Add(command, "skip", Math.Max(query.Skip, 0));
                DbHelpers.Add(command, "take", Math.Max(query.Take, 0));
            },
            (command, token) => DbHelpers.ReadListAsync(command, ReadJob, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Items live UNDER a job; the job id comes from a query already filtered by tenant (GetAsync/QueryAsync).")]
    public async ValueTask<IReadOnlyList<JobItemRecord>> ListItemsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ExecuteStatementAsync(
            _sql.SelectJobItems,
            command =>
            {
                DbHelpers.Add(command, "job_id", jobId);
            },
            (command, token) => DbHelpers.ReadListAsync(command, ReadJobItem, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "The item result is written by the worker running the job; the job id comes from LeaseAsync.")]
    public async ValueTask ReportItemAsync(JobItemResult item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        // 🚨 The report is two statements on SQL Server and SQLite (no data-modifying CTE): the item row, then the
        // counters of the job, which read how many rows the first one changed. In autocommit the first has committed
        // when the second is the deadlock victim, and sending the whole text again would find the item already reported
        // and count nothing. In a transaction a victim rolls both back, and the next attempt starts from the beginning.
        await _context.ExecuteInTransactionAsync(
            async (connection, transaction) =>
            {
                var command = _context.CreateCommand(_sql.ReportJobItem, connection, transaction);
                DbHelpers.Add(command, "job_id", item.JobId);
                DbHelpers.Add(command, "seq", item.Seq);
                DbHelpers.Add(command, "status", (short)item.Status);
                AddNullableUuid(command, "run_id", item.RunId);
                AddNullableText(command, "error", item.Error);

                await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Queue depth is an operator signal about the worker pool, which leases across EVERY tenant (LeaseAsync); there is no tenant boundary to apply and no tenant tag is published.")]
    public async ValueTask<IReadOnlyList<JobQueueDepth>> GetQueueDepthAsync(CancellationToken cancellationToken = default)
    {
        return await _context.ExecuteStatementAsync(
            _sql.SelectJobQueueDepth,
            static command => { },
            (command, token) => DbHelpers.ReadListAsync(command, ReadQueueDepth, token),
            cancellationToken).ConfigureAwait(false);
    }

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
            MaxDuration = reader.IsDBNull(20) ? null : TimeSpan.FromMilliseconds(reader.GetInt64(20)),
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

    /// <remarks>
    /// An unset value is written as the empty JSON ARRAY, not as the literal
    /// <c>null</c>: every column this helper feeds carries SQL Server's
    /// <c>CHECK (ISJSON(...) = 1)</c>, and <c>ISJSON(N'null')</c> is <c>0</c>.
    /// The records normalize on the way in (<c>FreeFormJson</c>), so this is
    /// the second line of the same rule, not the first.
    /// </remarks>
    private static string RawJson(JsonElement payload)
        => payload.ValueKind == JsonValueKind.Undefined ? "[]" : payload.GetRawText();

    private void AddNullableText(DbCommand command, string name, string? value)
        => Dialect.AddText(command, name, value);

    private void AddNullableUuid(DbCommand command, string name, Guid? value)
        => Dialect.AddUuid(command, name, value);
}
