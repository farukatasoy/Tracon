using System.Collections.Concurrent;

namespace AgentPrism;

/// <summary>
/// Kuyruktaki isleri ve ogelerini surec bellegi icinde tutan depo.
/// </summary>
/// <remarks>
/// <para>
/// Kiralama <c>_jobs</c> sozlugunun kendisi uzerinde kilitlenerek korunur
/// (net8.0 de hedeflendigi icin <c>System.Threading.Lock</c>
/// kullanilmaz — ayri bir kilit alani MA0158'i tetiklerdi): yarisan iki
/// <see cref="LeaseAsync"/> cagrisi ayni isi iki kez donduremez — tipki
/// PostgreSQL uygulamasinin <c>FOR UPDATE SKIP LOCKED</c> ile verdigi
/// sozlesme gibi.
/// </para>
/// <para>
/// <strong>Sinirlari:</strong> surec omru ve tek dugum. Uretimde
/// <c>AgentPrism.PostgreSql</c> kullanin.
/// </para>
/// </remarks>
public sealed class InMemoryJobStore : IJobStore
{
    private readonly ConcurrentDictionary<Guid, JobRecord> _jobs = new();
    private readonly ConcurrentDictionary<Guid, List<JobItemRecord>> _items = new();
    private readonly TimeProvider _clock;

    /// <summary>Yeni bir bellek ici is deposu olusturur.</summary>
    /// <param name="timeProvider">Zaman kaynagi. Verilmezse <see cref="TimeProvider.System"/> kullanilir.</param>
    public InMemoryJobStore(TimeProvider? timeProvider = null)
    {
        _clock = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public ValueTask<JobRecord> EnqueueAsync(
        JobRecord job,
        IReadOnlyList<string> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(items);

        var record = job with
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
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

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
    public ValueTask ReleaseForRetryAsync(Guid jobId, string errorMessage, CancellationToken cancellationToken = default)
    {
        lock (_jobs)
        {
            if (_jobs.TryGetValue(jobId, out var job))
            {
                _jobs[jobId] = job with
                {
                    Status = JobStatus.Pending,
                    LeaseOwner = null,
                    LeaseUntil = null,
                    ErrorMessage = errorMessage,
                };
            }
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask<bool> CancelAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

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

        var matches = new List<JobRecord>();

        foreach (var job in _jobs.Values)
        {
            if (query.TenantId is { } tenantId && !string.Equals(job.TenantId, tenantId, StringComparison.Ordinal))
            {
                continue;
            }

            if (query.Kind is { } kind && job.Kind != kind)
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

            // Idempotent: ayni oge kira suresi dolup yeniden islenirse sayaclar
            // yalnizca ilk raporlamada artar.
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
}
