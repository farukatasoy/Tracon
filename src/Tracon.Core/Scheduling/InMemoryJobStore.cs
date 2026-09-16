using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// A store that keeps queued jobs and their items in process memory.
/// </summary>
/// <remarks>
/// <para>
/// Leasing is protected by locking the <c>_jobs</c> dictionary itself. Since net8.0
/// is also targeted, it does not use <c>System.Threading.Lock</c>, because a separate
/// lock field would trigger MA0158. Two competing <see cref="LeaseAsync"/> calls cannot
/// return the same job. This matches the contract from <c>FOR UPDATE SKIP LOCKED</c> in PostgreSQL.
/// </para>
/// <para>
/// <strong>Limits:</strong> process lifetime and a single node. Use
/// <c>Tracon.PostgreSql</c> in production.
/// </para>
/// </remarks>
internal sealed class InMemoryJobStore : IJobStore
{
    private readonly ConcurrentDictionary<Guid, JobRecord> _jobs = new();
    private readonly ConcurrentDictionary<Guid, List<JobItemRecord>> _items = new();
    private readonly TimeProvider _clock;
    private readonly IOptionsMonitor<TraconSchedulingOptions>? _schedulingOptions;

    /// <summary>Initializes a new in-memory job store.</summary>
    /// <param name="timeProvider">The time provider. Uses <see cref="TimeProvider.System"/> when omitted.</param>
    /// <param name="schedulingOptions">
    /// The source for <c>LaneByHandlerKey</c>. <see langword="null"/> disables lane-by-key resolution.
    /// </param>
    public InMemoryJobStore(TimeProvider? timeProvider = null, IOptionsMonitor<TraconSchedulingOptions>? schedulingOptions = null)
    {
        _clock = timeProvider ?? TimeProvider.System;
        _schedulingOptions = schedulingOptions;
    }

    /// <inheritdoc />
    public ValueTask<JobRecord> EnqueueAsync(
        JobRecord job,
        IReadOnlyList<string> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(items);
        cancellationToken.ThrowIfCancellationRequested();

        var lane = JobLanes.Resolve(job.Lane, job.HandlerKey, _schedulingOptions?.CurrentValue.LaneByHandlerKey);

        var record = job with
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

        var itemRecords = new List<JobItemRecord>(items.Count);

        for (var seq = 0; seq < items.Count; seq++)
        {
            itemRecords.Add(new JobItemRecord
            {
                Id = Guid.NewGuid(),
                JobId = record.Id,
                Seq = seq,
                Input = items[seq],
                Status = JobItemStatus.Pending,
            });
        }

        _jobs[record.Id] = record;
        _items[record.Id] = itemRecords;

        return new ValueTask<JobRecord>(record);
    }

    /// <inheritdoc />
    public ValueTask<JobRecord?> LeaseAsync(
        string owner,
        TimeSpan leaseDuration,
        IReadOnlyList<string>? lanes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        cancellationToken.ThrowIfCancellationRequested();

        var now = _clock.GetUtcNow();

        lock (_jobs)
        {
            JobRecord? candidate = null;

            foreach (var job in _jobs.Values)
            {
                if (job.ScheduledFor > now)
                {
                    continue;
                }

                if (lanes is { Count: > 0 } && !lanes.Contains(job.Lane, StringComparer.Ordinal))
                {
                    continue;
                }

                var claimable = job.Status == JobStatus.Pending
                    || (job.Status is JobStatus.Leased or JobStatus.Running
                        && job.LeaseUntil is { } leaseUntil
                        && leaseUntil < now);

                if (!claimable)
                {
                    continue;
                }

                if (candidate is null || job.ScheduledFor < candidate.ScheduledFor)
                {
                    candidate = job;
                }
            }

            if (candidate is null)
            {
                return new ValueTask<JobRecord?>(result: null);
            }

            var leased = candidate with
            {
                Status = JobStatus.Leased,
                LeaseOwner = owner,
                LeaseUntil = now + leaseDuration,
                Attempt = candidate.Attempt + 1,
                StartedAt = candidate.StartedAt ?? now,
            };

            _jobs[leased.Id] = leased;

            return new ValueTask<JobRecord?>(leased);
        }
    }

    /// <inheritdoc />
    public ValueTask RenewLeaseAsync(
        Guid jobId,
        string owner,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_jobs)
        {
            if (_jobs.TryGetValue(jobId, out var job) && string.Equals(job.LeaseOwner, owner, StringComparison.Ordinal))
            {
                _jobs[jobId] = job with { LeaseUntil = _clock.GetUtcNow() + leaseDuration };
            }
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask<bool> MarkRunningAsync(Guid jobId, string owner, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_jobs)
        {
            if (_jobs.TryGetValue(jobId, out var job)
                && job.Status == JobStatus.Leased
                && string.Equals(job.LeaseOwner, owner, StringComparison.Ordinal))
            {
                _jobs[jobId] = job with { Status = JobStatus.Running };
                return new ValueTask<bool>(true);
            }

            return new ValueTask<bool>(false);
        }
    }

    /// <inheritdoc />
    public ValueTask CompleteAsync(JobCompletion completion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_jobs)
        {
            if (_jobs.TryGetValue(completion.JobId, out var job))
            {
                _jobs[completion.JobId] = job with
                {
                    Status = completion.Status,
                    CompletedAt = completion.CompletedAt,
                    ErrorMessage = completion.ErrorMessage,
                    LeaseOwner = null,
                    LeaseUntil = null,
                };
            }
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask ReleaseForRetryAsync(
        Guid jobId,
        string errorMessage,
        TimeSpan? retryAfter = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_jobs)
        {
            if (_jobs.TryGetValue(jobId, out var job))
            {
                // Backoff is represented through ScheduledFor, which leasing already
                // filters. There is no separate "next attempt" field.
                var scheduledFor = retryAfter is { } delay && delay > TimeSpan.Zero
                    ? _clock.GetUtcNow() + delay
                    : job.ScheduledFor;

                _jobs[jobId] = job with
                {
                    Status = JobStatus.Pending,
                    LeaseOwner = null,
                    LeaseUntil = null,
                    ErrorMessage = errorMessage,
                    ScheduledFor = scheduledFor,
                };
            }
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask<bool> CancelAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_jobs)
        {
            if (!_jobs.TryGetValue(jobId, out var job)
                || !string.Equals(job.TenantId, tenantId, StringComparison.Ordinal))
            {
                return new ValueTask<bool>(false);
            }

            if (job.Status is not (JobStatus.Pending or JobStatus.Leased or JobStatus.Running))
            {
                return new ValueTask<bool>(false);
            }

            _jobs[jobId] = job with
            {
                Status = JobStatus.Cancelled,
                CompletedAt = _clock.GetUtcNow(),
                LeaseOwner = null,
                LeaseUntil = null,
            };

            return new ValueTask<bool>(true);
        }
    }

    /// <inheritdoc />
    public ValueTask<JobRecord?> GetAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        cancellationToken.ThrowIfCancellationRequested();

        if (_jobs.TryGetValue(jobId, out var job) && string.Equals(job.TenantId, tenantId, StringComparison.Ordinal))
        {
            return new ValueTask<JobRecord?>(job);
        }

        return new ValueTask<JobRecord?>(result: null);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<JobRecord>> QueryAsync(JobQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var matches = new List<JobRecord>();

        foreach (var job in _jobs.Values)
        {
            if (query.TenantId is { } tenantId && !string.Equals(job.TenantId, tenantId, StringComparison.Ordinal))
            {
                continue;
            }

            if (query.HandlerKey is { } handlerKey && !string.Equals(job.HandlerKey, handlerKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (query.Lane is { } lane && !string.Equals(job.Lane, lane, StringComparison.Ordinal))
            {
                continue;
            }

            if (query.Status is { } status && job.Status != status)
            {
                continue;
            }

            if (query.ScheduleId is { } scheduleId && job.ScheduleId != scheduleId)
            {
                continue;
            }

            matches.Add(job);
        }

        matches.Sort(static (left, right) => right.CreatedAt.CompareTo(left.CreatedAt));

        var start = Math.Clamp(query.Skip, 0, matches.Count);
        var count = Math.Clamp(query.Take, 0, matches.Count - start);

        return new ValueTask<IReadOnlyList<JobRecord>>(matches.GetRange(start, count));
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<JobItemRecord>> ListItemsAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_items.TryGetValue(jobId, out var log))
        {
            return new ValueTask<IReadOnlyList<JobItemRecord>>([]);
        }

        JobItemRecord[] snapshot;

        lock (log)
        {
            snapshot = [.. log];
        }

        Array.Sort(snapshot, static (left, right) => left.Seq.CompareTo(right.Seq));

        return new ValueTask<IReadOnlyList<JobItemRecord>>(snapshot);
    }

    /// <inheritdoc />
    public ValueTask ReportItemAsync(JobItemResult item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_items.TryGetValue(item.JobId, out var log))
        {
            return default;
        }

        lock (log)
        {
            var index = log.FindIndex(candidate => candidate.Seq == item.Seq);

            if (index < 0)
            {
                return default;
            }

            var previousStatus = log[index].Status;

            log[index] = log[index] with
            {
                Status = item.Status,
                RunId = item.RunId,
                Error = item.Error,
            };

            // Idempotent: if the same item is processed again after its lease expires,
            // counters increase only for the first report.
            if (previousStatus == JobItemStatus.Pending)
            {
                lock (_jobs)
                {
                    if (_jobs.TryGetValue(item.JobId, out var job))
                    {
                        _jobs[item.JobId] = job with
                        {
                            DoneItems = job.DoneItems + (item.Status == JobItemStatus.Completed ? 1 : 0),
                            FailedItems = job.FailedItems + (item.Status == JobItemStatus.Failed ? 1 : 0),
                        };
                    }
                }
            }
        }

        return default;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Deliberately tenant-agnostic, like <see cref="LeaseAsync"/>: queue depth
    /// is an operator signal about the worker pool, which leases across every
    /// tenant. (The <c>TenantAgnostic</c> marker itself lives in the SQL linked
    /// source and is not reachable from this assembly; the SQL implementation
    /// carries it.)
    /// </remarks>
    public ValueTask<IReadOnlyList<JobQueueDepth>> GetQueueDepthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var counts = new Dictionary<(string Lane, JobStatus Status), long>();

        foreach (var job in _jobs.Values)
        {
            // Only the OPEN statuses. Terminal ones are counted by the
            // tracon.job.executions counter as each job finishes.
            if (job.Status is not (JobStatus.Pending or JobStatus.Leased or JobStatus.Running))
            {
                continue;
            }

            var key = (job.Lane, job.Status);
            counts[key] = counts.TryGetValue(key, out var current) ? current + 1 : 1;
        }

        var depths = new List<JobQueueDepth>(counts.Count);

        foreach (var ((lane, status), count) in counts)
        {
            depths.Add(new JobQueueDepth { Lane = lane, Status = status, Count = count });
        }

        return new ValueTask<IReadOnlyList<JobQueueDepth>>(depths);
    }
}
