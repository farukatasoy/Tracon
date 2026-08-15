using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Drops due schedules into the job queue, and picks up leasable jobs to
/// dispatch to registered <see cref="IJobHandler"/> implementations.
/// </summary>
/// <remarks>
/// <para>
/// If <see cref="AgentPrismSchedulingOptions.RunWorker"/> is <see langword="false"/>,
/// <see cref="ExecuteAsync"/> returns immediately and sets up no timer — the
/// same "an idle setup makes no background calls" rule as
/// <c>ModelProviderHealthBackgroundService</c>. The job and schedule stores
/// keep working regardless; only this process leases no jobs.
/// </para>
/// <para>
/// If a tick fails, the error is logged and the next tick runs normally —
/// the "observability must not break functionality / background errors must
/// not break functionality" rule applies here too.
/// </para>
/// </remarks>
internal sealed class JobWorkerBackgroundService(
    IJobStore jobStore,
    IJobScheduleStore scheduleStore,
    IEnumerable<IJobHandler> handlers,
    IOptionsMonitor<AgentPrismSchedulingOptions> optionsMonitor,
    SchemaReadyGate schemaReadyGate,
    TimeProvider? timeProvider = null,
    ILogger<JobWorkerBackgroundService>? logger = null) : BackgroundService
{
    private readonly string _ownerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = optionsMonitor.CurrentValue;

        if (!options.Enabled || !options.RunWorker || options.PollInterval <= TimeSpan.Zero)
        {
            return;
        }

        // 🚨 Wait for the schema to be ready BEFORE the first SQL attempt.
        // PeriodicTimer gives one tick of delay, but that is NOT a guarantee:
        // with a short PollInterval, the migration may not have finished yet.
        // Rationale: K-354.
        try
        {
            await schemaReadyGate.WaitAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.PollInterval);
        using var slots = new SemaphoreSlim(Math.Max(1, options.MaxConcurrentJobs));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await TickAsync(slots, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task TickAsync(SemaphoreSlim slots, CancellationToken stoppingToken)
    {
        try
        {
            await DispatchDueSchedulesAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "The schedule-dispatch tick failed.");
            }
        }

        var options = optionsMonitor.CurrentValue;

        while (await slots.WaitAsync(0, stoppingToken).ConfigureAwait(false))
        {
            JobRecord? job;

            try
            {
                job = await jobStore.LeaseAsync(_ownerId, options.LeaseDuration, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                slots.Release();

                if (logger is not null && logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning(exception, "Job leasing failed.");
                }

                break;
            }

            if (job is null)
            {
                slots.Release();
                break;
            }

            _ = RunJobAsync(job, slots, stoppingToken);
        }
    }

    private async Task RunJobAsync(JobRecord job, SemaphoreSlim slots, CancellationToken stoppingToken)
    {
        try
        {
            await ExecuteJobAsync(job, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "Job execution failed unexpectedly: {JobId}.", job.Id);
            }
        }
        finally
        {
            slots.Release();
        }
    }

    private async Task ExecuteJobAsync(JobRecord job, CancellationToken stoppingToken)
    {
        var options = optionsMonitor.CurrentValue;
        var handler = handlers.FirstOrDefault(candidate => candidate.Kind == job.Kind);
        var now = _clock.GetUtcNow();

        if (handler is null)
        {
            await jobStore.CompleteAsync(
                new JobCompletion
                {
                    JobId = job.Id,
                    Status = JobStatus.Failed,
                    CompletedAt = now,
                    ErrorMessage = $"No IJobHandler is registered for kind '{job.Kind}'.",
                },
                stoppingToken).ConfigureAwait(false);

            return;
        }

        await jobStore.MarkRunningAsync(job.Id, _ownerId, stoppingToken).ConfigureAwait(false);

        var items = await jobStore.ListItemsAsync(job.Id, stoppingToken).ConfigureAwait(false);

        var context = new JobContext
        {
            Job = job,
            Items = items,
            ReportItemAsync = (result, ct) => jobStore.ReportItemAsync(result, ct),
            IsCancelledAsync = async ct =>
            {
                var current = await jobStore.GetAsync(job.TenantId, job.Id, ct).ConfigureAwait(false);
                return current is null || current.Status == JobStatus.Cancelled;
            },
        };

        using var tenantScope = AmbientTenantScope.Begin(job.TenantId);
        using var leaseRenewal = StartLeaseRenewal(job.Id, options.LeaseDuration, stoppingToken);

        try
        {
            await handler.ExecuteAsync(context, stoppingToken).ConfigureAwait(false);

            var current = await jobStore.GetAsync(job.TenantId, job.Id, stoppingToken).ConfigureAwait(false);

            if (current?.Status == JobStatus.Cancelled)
            {
                // The handler already saw this through IsCancelledAsync between
                // items and exited early; do not overwrite with CompleteAsync.
                return;
            }

            var finalItems = await jobStore.ListItemsAsync(job.Id, stoppingToken).ConfigureAwait(false);
            var allFailed = finalItems.Count > 0 && finalItems.All(item => item.Status == JobItemStatus.Failed);

            await jobStore.CompleteAsync(
                new JobCompletion
                {
                    JobId = job.Id,
                    Status = allFailed ? JobStatus.Failed : JobStatus.Completed,
                    CompletedAt = _clock.GetUtcNow(),
                    ErrorMessage = allFailed ? "All of the job's items failed." : null,
                },
                stoppingToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The job may carry its own attempt limit (webhook delivery uses a
            // different ladder than the global setting); if not, the global setting.
            var maxAttempts = job.MaxAttempts ?? options.MaxAttempts;

            if (job.Attempt >= maxAttempts)
            {
                await jobStore.CompleteAsync(
                    new JobCompletion
                    {
                        JobId = job.Id,
                        Status = JobStatus.Failed,
                        CompletedAt = _clock.GetUtcNow(),
                        ErrorMessage = exception.Message,
                    },
                    stoppingToken).ConfigureAwait(false);
            }
            else
            {
                // The handler may request a delay; if not, the old behavior
                // (immediately re-leasable) is preserved.
                var retryAfter = (exception as JobRetryException)?.RetryAfter;

                await jobStore
                    .ReleaseForRetryAsync(job.Id, exception.Message, retryAfter, stoppingToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task DispatchDueSchedulesAsync(CancellationToken stoppingToken)
    {
        var now = _clock.GetUtcNow();
        var due = await scheduleStore.ListDueAsync(now, stoppingToken).ConfigureAwait(false);

        foreach (var schedule in due)
        {
            if (schedule.NextRunAt is not { } nextRunAt || schedule.Cron is not { Length: > 0 } cron)
            {
                continue;
            }

            DateTimeOffset newNextRunAt;

            try
            {
                var timeZone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZone);
                newNextRunAt = CronExpression.Parse(cron).GetNextOccurrence(now, timeZone);
            }
            catch (Exception exception) when (
                exception is FormatException or TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                if (logger is not null && logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning(exception, "Schedule '{Name}' could not be resolved; skipping this tick.", schedule.Name);
                }

                continue;
            }

            // Another instance may have claimed the same schedule at the same
            // time; the job is only created if this instance's claim wins.
            var claimed = await scheduleStore
                .TryClaimNextRunAsync(schedule.Id, nextRunAt, newNextRunAt, now, stoppingToken)
                .ConfigureAwait(false);

            if (!claimed)
            {
                continue;
            }

            await jobStore.EnqueueAsync(
                new JobRecord
                {
                    Id = AgentPrismId.NewId(),
                    TenantId = schedule.TenantId,
                    ScheduleId = schedule.Id,
                    Kind = schedule.Kind,
                    TargetName = schedule.TargetName,
                    Status = JobStatus.Pending,
                    Payload = schedule.Payload,
                    ScheduledFor = nextRunAt,
                    CreatedAt = now,
                },
                JobPayload.ExtractItems(schedule.Payload),
                stoppingToken).ConfigureAwait(false);
        }
    }

    private CancelOnDispose StartLeaseRenewal(Guid jobId, TimeSpan leaseDuration, CancellationToken stoppingToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var interval = leaseDuration / 2;

        if (interval <= TimeSpan.Zero)
        {
            interval = TimeSpan.FromSeconds(30);
        }

        _ = RenewLoopAsync(jobId, leaseDuration, interval, cts.Token);

        return new CancelOnDispose(cts);
    }

    private async Task RenewLoopAsync(Guid jobId, TimeSpan leaseDuration, TimeSpan interval, CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(interval);

            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                await jobStore.RenewLeaseAsync(jobId, _ownerId, leaseDuration, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // The job finished, or the worker is shutting down.
        }
        catch (Exception exception)
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "Lease renewal failed: {JobId}.", jobId);
            }
        }
    }

    /// <summary>A helper that cancels, then disposes, a <see cref="CancellationTokenSource"/>.</summary>
    private sealed class CancelOnDispose(CancellationTokenSource cts) : IDisposable
    {
        public void Dispose()
        {
            cts.Cancel();
            cts.Dispose();
        }
    }
}
