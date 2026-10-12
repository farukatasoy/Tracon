using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Drops due schedules into the job queue, and picks up leasable jobs to
/// dispatch to registered <see cref="IJobHandler"/> implementations.
/// </summary>
/// <remarks>
/// <para>
/// If <see cref="TraconSchedulingOptions.RunWorker"/> is <see langword="false"/>,
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
    JobHandlerRegistry registry,
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<TraconSchedulingOptions> optionsMonitor,
    SchemaReadyGate schemaReadyGate,
    ITraconDrainState drainState,
    TimeProvider? timeProvider = null,
    ILogger<JobWorkerBackgroundService>? logger = null,
    TraconMetrics? metrics = null) : BackgroundService
{
    /// <summary>
    /// The handler-key metric tag published for a job whose key no handler is
    /// registered for. A constant, so arbitrary text on a job row cannot give
    /// <c>tracon.job.executions</c> unbounded cardinality.
    /// </summary>
    internal const string UnregisteredHandlerKeyTag = "unregistered";

    /// <summary>The pause before a failed finalization store call is tried again.</summary>
    private static readonly TimeSpan FinalizationRetryFirstDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>The longest pause between two finalization tries; the pause doubles up to it.</summary>
    private static readonly TimeSpan FinalizationRetryMaxDelay = TimeSpan.FromSeconds(5);

    private readonly string _ownerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<Guid, Task> _runningJobs = new();

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
        using var slots = new WorkerSlots(options.MaxConcurrentJobs);

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
        finally
        {
            // TickAsync leases jobs as concurrent work. The semaphore must stay
            // alive until every leased job has released its slot, including when
            // the host stops before the work itself has observed cancellation.
            await WaitForRunningJobsAsync().ConfigureAwait(false);
        }
    }

    private async Task TickAsync(WorkerSlots slots, CancellationToken stoppingToken)
    {
        // 🚨 Phase 87: while the process is draining, no NEW work starts --
        // neither a schedule-dispatched job nor a leased one. Jobs already
        // running (RunJobAsync, fire-and-forget) are untouched here; they
        // finish on their own, and TraconDrainService's StopAsync is what
        // actually waits for them.
        if (drainState.IsDraining)
        {
            return;
        }

        try
        {
            await DispatchDueSchedulesAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, stoppingToken))
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "The schedule-dispatch tick failed.");
            }
        }

        var options = optionsMonitor.CurrentValue;

        // A lane listed in MaxConcurrentJobsPerLane gets its OWN lease pass,
        // scoped to exactly that lane, so a full lane never blocks another
        // lane's slot (129.4's starvation guarantee) and never gets leased
        // beyond its own budget by the shared pass below.
        foreach (var (lane, max) in options.MaxConcurrentJobsPerLane)
        {
            if (options.Lanes is { } subscribed && !subscribed.Contains(lane, StringComparer.Ordinal))
            {
                continue;
            }

            var semaphore = slots.ForLane(lane, max);
            await LeaseLoopAsync(semaphore, [lane], options.LeaseDuration, stoppingToken).ConfigureAwait(false);
        }

        var sharedLanes = ComputeSharedLanes(options);

        // A non-null, empty list means every subscribed lane already has its
        // own dedicated pass above; there is nothing left for the shared pool.
        if (sharedLanes is null || sharedLanes.Length > 0)
        {
            await LeaseLoopAsync(slots.Shared, sharedLanes, options.LeaseDuration, stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Computes the lane filter for the shared-pool lease pass — every
    /// subscribed lane MINUS the ones already covered by their own dedicated
    /// pass in <see cref="TickAsync"/>.
    /// </summary>
    /// <remarks>
    /// If <see cref="TraconSchedulingOptions.Lanes"/> is <see langword="null"/>
    /// (unrestricted) and no per-lane budget is configured, this returns
    /// <see langword="null"/> too — the exact pre-lane behavior. Once a
    /// per-lane budget exists, an unrestricted worker's shared pass narrows to
    /// <see cref="JobLanes.Default"/>: a plain include-list filter (the only
    /// kind <c>IJobStore.LeaseAsync</c> supports) cannot express "every lane
    /// except these", so an open-ended subscription combined with a per-lane
    /// budget stops covering an arbitrary third lane nobody budgeted for. See
    /// the XML remarks on <see cref="TraconSchedulingOptions.MaxConcurrentJobsPerLane"/>.
    /// </remarks>
    private static string[]? ComputeSharedLanes(TraconSchedulingOptions options)
    {
        var named = options.MaxConcurrentJobsPerLane;

        if (options.Lanes is { } subscribed)
        {
            return subscribed.Where(lane => !named.ContainsKey(lane)).ToArray();
        }

        return named.Count == 0 ? null : [JobLanes.Default];
    }

    private async Task LeaseLoopAsync(
        SemaphoreSlim semaphore,
        IReadOnlyList<string>? lanes,
        TimeSpan leaseDuration,
        CancellationToken stoppingToken)
    {
        while (await semaphore.WaitAsync(0, stoppingToken).ConfigureAwait(false))
        {
            JobRecord? job;

            // Taken BEFORE the call: the store starts the lease at or after
            // this moment, so the lease this worker counts on never outlasts
            // the store's (F-348).
            var leaseRequestedAt = _clock.GetTimestamp();

            try
            {
                job = await jobStore.LeaseAsync(_ownerId, leaseDuration, lanes, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (OperationCancellation.IsFailure(exception, stoppingToken))
            {
                semaphore.Release();

                if (logger is not null && logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning(exception, "Job leasing failed.");
                }

                break;
            }

            if (job is null)
            {
                semaphore.Release();
                break;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            if (!_runningJobs.TryAdd(job.Id, completion.Task))
            {
                semaphore.Release();

                if (logger is not null && logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Job {JobId} is already executing on this worker.", job.Id);
                }

                continue;
            }

            _ = RunJobAsync(job, leaseRequestedAt, semaphore, completion, stoppingToken);
        }
    }

    private async Task RunJobAsync(
        JobRecord job,
        long leaseRequestedAt,
        SemaphoreSlim semaphore,
        TaskCompletionSource completion,
        CancellationToken stoppingToken)
    {
        try
        {
            await ExecuteJobAsync(job, leaseRequestedAt, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, stoppingToken))
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "Job execution failed unexpectedly: {JobId}.", job.Id);
            }
        }
        finally
        {
            semaphore.Release();
            _runningJobs.TryRemove(job.Id, out _);
            completion.TrySetResult();
        }
    }

    private async Task WaitForRunningJobsAsync()
    {
        while (!_runningJobs.IsEmpty)
        {
            await Task.WhenAll(_runningJobs.Values).ConfigureAwait(false);
        }
    }

    private async Task ExecuteJobAsync(JobRecord job, long leaseRequestedAt, CancellationToken stoppingToken)
    {
        var options = optionsMonitor.CurrentValue;
        var now = _clock.GetUtcNow();

        // MONOTONIC clock, not the wall clock: a clock adjustment mid-attempt
        // must not be able to produce a negative duration.
        var startedAt = _clock.GetTimestamp();

        if (!registry.TryGetHandlerType(job.HandlerKey, out var handlerType))
        {
            var unknownKeyRef = SafeErrorText.NewCorrelationId();

            // 🚨 The raw key goes to the LOG, never to the persisted message:
            // a key nobody registered may have come from an untrusted source,
            // and ErrorMessage is read back over HTTP and shown in the UI.
            if (logger is not null && logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(
                    "Job {JobId} carries the handler key '{HandlerKey}', which no IJobHandler is registered for. (ref: {CorrelationId})",
                    job.Id,
                    job.HandlerKey,
                    unknownKeyRef);
            }

            // 🚨 The metric tag is a CONSTANT here, not the key. The registered
            // key set is fixed at host start and therefore bounded, but a key
            // nobody registered is arbitrary text from an untrusted source --
            // publishing it would give the tag unbounded cardinality, the same
            // hazard ResolveLaneTag guards for lanes. The raw key is in the log,
            // under the same correlation id.
            await FailWithoutRunningAsync(
                job,
                JobErrorCodes.Format(JobErrorCodes.UnknownHandlerKey, unknownKeyRef),
                now,
                startedAt,
                UnregisteredHandlerKeyTag,
                stoppingToken).ConfigureAwait(false);

            return;
        }

        // 🚨 F-326: the lease query re-leases a job whose lease ran out and
        // counts the attempt, but only the handler-failure path below compared
        // the count with the limit. A worker that died mid-job (crash, SIGKILL,
        // a lost lease) therefore had its job run again with no limit — even
        // under MaxAttempts = 1, the promise a queued agent run is made with.
        // The check sits here, in the worker, not in each store's lease query:
        // the global default is not in the database, and a third-party store
        // gets the same guarantee.
        var maxAttempts = job.MaxAttempts ?? options.MaxAttempts;

        if (job.Attempt > maxAttempts)
        {
            var exhaustedRef = SafeErrorText.NewCorrelationId();

            if (logger is not null && logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(
                    "Job {JobId} was leased again after its lease ran out, and attempt {Attempt} is past its limit of {MaxAttempts}; it is failed without running. (ref: {CorrelationId})",
                    job.Id,
                    job.Attempt,
                    maxAttempts,
                    exhaustedRef);
            }

            await FailWithoutRunningAsync(
                job,
                JobErrorCodes.Format(JobErrorCodes.AttemptsExhausted, exhaustedRef),
                now,
                startedAt,
                job.HandlerKey,
                stoppingToken).ConfigureAwait(false);

            return;
        }

        // 🚨 The ambient tenant is opened HERE, in the method that owns the
        // whole execution, not inside the helper below: an AsyncLocal write
        // made in an async helper does not flow back to its caller. It also has
        // to precede the container call -- a scoped dependency may read
        // ITenantContext while it is being constructed.
        using var tenantScope = AmbientTenantScope.Begin(job.TenantId);

        // 🚨 One scope per EXECUTION, not per process: two jobs running in
        // parallel, and two attempts of the same job, each get their own
        // handler instance and their own scoped dependencies. The scope stays
        // open until ExecuteAsync returns.
        var scope = scopeFactory.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            IJobHandler handler;

            try
            {
                handler = (IJobHandler)scope.ServiceProvider.GetRequiredService(handlerType);
            }
            catch (Exception exception) when (OperationCancellation.IsFailure(exception, stoppingToken))
            {
                // 🚨 Before the handler resolves, NOTHING owns the job's outcome:
                // the attempt limit lives in ExecuteWithHandlerAsync's catch, and
                // the caller of this method only logs. Letting an activation
                // failure escape would leave the job re-leased forever, once per
                // lease expiry, with no terminal status. A missing dependency is a
                // configuration mistake, so retrying cannot help either — fail it.
                var activationRef = SafeErrorText.NewCorrelationId();

                if (logger is not null && logger.IsEnabled(LogLevel.Error))
                {
                    logger.LogError(
                        exception,
                        "The job handler registered for '{HandlerKey}' could not be constructed. (ref: {CorrelationId})",
                        job.HandlerKey,
                        activationRef);
                }

                await FailWithoutRunningAsync(
                    job,
                    JobErrorCodes.Format(JobErrorCodes.HandlerActivationFailed, activationRef),
                    _clock.GetUtcNow(),
                    startedAt,
                    job.HandlerKey,
                    stoppingToken).ConfigureAwait(false);

                return;
            }

            await ExecuteWithHandlerAsync(job, handler, options, startedAt, leaseRequestedAt, stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Closes a job that never reached its handler, and counts the attempt.
    /// </summary>
    /// <param name="job">The leased job.</param>
    /// <param name="errorMessage">The redacted message, already carrying a stable code.</param>
    /// <param name="completedAt">The wall-clock completion time.</param>
    /// <param name="startedAt">The monotonic timestamp taken when the attempt started.</param>
    /// <param name="handlerKeyTag">
    /// The value to publish as the handler-key metric tag. The job's own key
    /// when it is registered; <see cref="UnregisteredHandlerKeyTag"/> when it
    /// is not, so an arbitrary key cannot give the tag unbounded cardinality.
    /// </param>
    /// <param name="stoppingToken">The worker's shutdown token.</param>
    private async Task FailWithoutRunningAsync(
        JobRecord job,
        string errorMessage,
        DateTimeOffset completedAt,
        long startedAt,
        string handlerKeyTag,
        CancellationToken stoppingToken)
    {
        await jobStore.CompleteAsync(
            new JobCompletion
            {
                JobId = job.Id,
                Status = JobStatus.Failed,
                CompletedAt = completedAt,
                ErrorMessage = errorMessage,
            },
            stoppingToken).ConfigureAwait(false);

        RecordJobMetric(job, JobStatus.Failed, startedAt, handlerKeyTag);
    }

    private async Task ExecuteWithHandlerAsync(
        JobRecord job,
        IJobHandler handler,
        TraconSchedulingOptions options,
        long startedAt,
        long leaseRequestedAt,
        CancellationToken stoppingToken)
    {
        if (!await jobStore.MarkRunningAsync(job.Id, _ownerId, stoppingToken).ConfigureAwait(false))
        {
            // The job left Leased between the lease and here: it was cancelled,
            // or its lease went to another worker. Either way it is not this
            // worker's to run (F-326; the result used to be ignored, and a job
            // cancelled in that window ran anyway).
            var latest = await jobStore.GetAsync(job.TenantId, job.Id, stoppingToken).ConfigureAwait(false);

            if (latest?.Status == JobStatus.Cancelled)
            {
                RecordJobMetric(job, JobStatus.Cancelled, startedAt);
            }
            else if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning("Job {JobId} is no longer leased by this worker; it is not run.", job.Id);
            }

            return;
        }

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

        // The renewal stays on until the outcome below is written, not only
        // while the handler runs: a store error is retried under a live lease.
        using var leaseRenewal = StartLeaseRenewal(job.Id, options.LeaseDuration, leaseRequestedAt, stoppingToken);

        Exception? failure = null;

        // 🚨 F-348: ONLY the handler sits in this try. The store calls that
        // write its outcome used to sit in it too, so a store error after a
        // handler that had succeeded counted as the handler's failure: with
        // attempts left the job was released and the handler ran again (a
        // second model call, repeated tool side effects); on its last attempt
        // the job became Failed while its work had finished. The handler's
        // outcome is decided here; the store calls below only write it.
        try
        {
            await handler.ExecuteAsync(context, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, stoppingToken))
        {
            failure = exception;
        }

        var window = new FinalizationWindow(_clock, leaseRenewal.ProvableRemaining);

        if (failure is null)
        {
            await FinalizeSucceededAsync(job, window, startedAt, stoppingToken).ConfigureAwait(false);
        }
        else
        {
            // The job may carry its own attempt limit (webhook delivery uses a
            // different ladder than the global setting); if not, the global setting.
            var maxAttempts = job.MaxAttempts ?? options.MaxAttempts;

            await FinalizeFailedAsync(job, failure, maxAttempts, window, startedAt, stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes the outcome of a handler that returned normally:
    /// <see cref="JobStatus.Completed"/>, or <see cref="JobStatus.Failed"/>
    /// when every item failed.
    /// </summary>
    /// <param name="job">The leased job.</param>
    /// <param name="window">How long a store error may be retried.</param>
    /// <param name="startedAt">The monotonic timestamp taken when the attempt started.</param>
    /// <param name="stoppingToken">The worker's shutdown token.</param>
    private async Task FinalizeSucceededAsync(
        JobRecord job,
        FinalizationWindow window,
        long startedAt,
        CancellationToken stoppingToken)
    {
        IReadOnlyList<JobItemRecord> finalItems = [];

        var listed = await RetryFinalizationAsync(
            job,
            "read its items",
            async ct => finalItems = await jobStore.ListItemsAsync(job.Id, ct).ConfigureAwait(false),
            window,
            stoppingToken).ConfigureAwait(false);

        if (!listed)
        {
            return;
        }

        var allFailed = finalItems.Count > 0 && finalItems.All(item => item.Status == JobItemStatus.Failed);

        var completion = new JobCompletion
        {
            JobId = job.Id,
            Status = allFailed ? JobStatus.Failed : JobStatus.Completed,
            CompletedAt = _clock.GetUtcNow(),
            ErrorMessage = allFailed ? "All of the job's items failed." : null,
        };

        var completed = await RetryFinalizationAsync(
            job,
            "complete it",
            ct => jobStore.CompleteAsync(completion, ct),
            window,
            stoppingToken).ConfigureAwait(false);

        if (!completed)
        {
            return;
        }

        // A job cancelled while its handler ran (the handler saw it through
        // IsCancelledAsync and exited early) is not overwritten: the store
        // leaves a terminal job unchanged. Only a read can tell which status
        // the job now has, and that read decides the metric and nothing else.
        // Cancelled IS terminal, so the attempt is still counted.
        var finalStatus = await TryReadStatusAsync(job, stoppingToken).ConfigureAwait(false) == JobStatus.Cancelled
            ? JobStatus.Cancelled
            : completion.Status;

        RecordJobMetric(job, finalStatus, startedAt);
    }

    /// <summary>
    /// Writes the outcome of a handler that threw: released for another
    /// attempt, or <see cref="JobStatus.Failed"/> on the last one.
    /// </summary>
    /// <param name="job">The leased job.</param>
    /// <param name="exception">What the handler threw.</param>
    /// <param name="maxAttempts">The job's attempt limit.</param>
    /// <param name="window">How long a store error may be retried.</param>
    /// <param name="startedAt">The monotonic timestamp taken when the attempt started.</param>
    /// <param name="stoppingToken">The worker's shutdown token.</param>
    private async Task FinalizeFailedAsync(
        JobRecord job,
        Exception exception,
        int maxAttempts,
        FinalizationWindow window,
        long startedAt,
        CancellationToken stoppingToken)
    {
        var correlationId = SafeErrorText.NewCorrelationId();

        if (logger is not null && logger.IsEnabled(LogLevel.Error))
        {
            logger.LogError(exception, "Job {JobId} failed. (ref: {CorrelationId})", job.Id, correlationId);
        }

        var safeMessage = SafeErrorText.ForPersistence(exception, correlationId);

        // 🚨 Cancelled is terminal (F-326). A handler that saw the cancel and
        // threw on its way out used to be treated as a plain failure: with
        // attempts left the job went back to Pending and ran again, on the
        // last attempt it became Failed. The store refuses both writes on a
        // terminal job too; this read is what keeps the metric honest, so a
        // read that fails does not hold back the write below (F-348).
        if (await TryReadStatusAsync(job, stoppingToken).ConfigureAwait(false) == JobStatus.Cancelled)
        {
            RecordJobMetric(job, JobStatus.Cancelled, startedAt);

            return;
        }

        if (job.Attempt >= maxAttempts)
        {
            var completion = new JobCompletion
            {
                JobId = job.Id,
                Status = JobStatus.Failed,
                CompletedAt = _clock.GetUtcNow(),
                ErrorMessage = safeMessage,
            };

            var failed = await RetryFinalizationAsync(
                job,
                "fail it",
                ct => jobStore.CompleteAsync(completion, ct),
                window,
                stoppingToken).ConfigureAwait(false);

            if (failed)
            {
                RecordJobMetric(job, JobStatus.Failed, startedAt);
            }

            return;
        }

        // The handler may request a delay; if not, the old behavior
        // (immediately re-leasable) is preserved.
        var retryAfter = (exception as JobRetryException)?.RetryAfter;

        await RetryFinalizationAsync(
            job,
            "release it for another attempt",
            ct => jobStore.ReleaseForRetryAsync(job.Id, safeMessage, retryAfter, ct),
            window,
            stoppingToken).ConfigureAwait(false);

        // 🚨 Deliberately NOT counted. The job has not finished; it will be
        // leased again. Counting a release for retry would turn
        // tracon.job.executions from "how many jobs finished" into "how many
        // things happened", and a job with MaxAttempts=3 that eventually
        // fails would be counted three times instead of once.
    }

    /// <summary>
    /// Runs one store call that finalizes a job whose handler has already
    /// returned, and tries it again after a store error, with a pause that
    /// doubles each time.
    /// </summary>
    /// <param name="job">The job being finalized.</param>
    /// <param name="step">What the call does, for the log.</param>
    /// <param name="call">The store call.</param>
    /// <param name="window">How long the tries may go on.</param>
    /// <param name="stoppingToken">The worker's shutdown token.</param>
    /// <returns>
    /// <see langword="true"/> once the call succeeded; <see langword="false"/>
    /// when the window ran out first. The job is then left to the lease-expiry
    /// path: it is leased again once its lease runs out, and that lease counts
    /// as an attempt.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The handler never runs again from here: a store error belongs to the
    /// store, not to the handler. Repeating a call is safe because the store
    /// leaves a terminal job unchanged.
    /// </para>
    /// <para>
    /// The tries stop at the window's end because the store's completion and
    /// release calls carry no owner check: once the lease this worker can
    /// prove has run out, another worker may own the job, and a late write
    /// would close or requeue that worker's attempt. Shutdown ends the tries
    /// too; its cancellation propagates like that of any other store call.
    /// </para>
    /// </remarks>
    private async Task<bool> RetryFinalizationAsync(
        JobRecord job,
        string step,
        Func<CancellationToken, ValueTask> call,
        FinalizationWindow window,
        CancellationToken stoppingToken)
    {
        var delay = FinalizationRetryFirstDelay;

        while (true)
        {
            try
            {
                await call(stoppingToken).ConfigureAwait(false);

                return true;
            }
            catch (Exception exception) when (OperationCancellation.IsFailure(exception, stoppingToken))
            {
                var left = window.Left;

                if (left <= TimeSpan.Zero)
                {
                    var correlationId = SafeErrorText.NewCorrelationId();

                    if (logger is not null && logger.IsEnabled(LogLevel.Error))
                    {
                        logger.LogError(
                            exception,
                            "Job {JobId}: could not {Step} before the lease this worker can prove ran out. The job is left to lease expiry: it is leased again and that lease counts as an attempt. (ref: {CorrelationId})",
                            job.Id,
                            step,
                            correlationId);
                    }

                    return false;
                }

                if (delay > left)
                {
                    delay = left;
                }

                if (logger is not null && logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning(exception, "Job {JobId}: could not {Step}; trying again in {Delay}.", job.Id, step, delay);
                }
            }

            await Task.Delay(delay, _clock, stoppingToken).ConfigureAwait(false);

            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, FinalizationRetryMaxDelay.Ticks));
        }
    }

    /// <summary>
    /// Reads a job's current status to label its metric. A store error is
    /// logged and answers <see langword="null"/>.
    /// </summary>
    /// <param name="job">The job to read.</param>
    /// <param name="stoppingToken">The worker's shutdown token.</param>
    /// <returns>The job's status, or <see langword="null"/> when the read failed or found no job.</returns>
    /// <remarks>
    /// The read decides only the metric and whether a cancelled job is written
    /// at all; the store already refuses a write on a terminal job. Observability
    /// must not break functionality, so a failed read never holds back the
    /// write that finalizes the job.
    /// </remarks>
    private async Task<JobStatus?> TryReadStatusAsync(JobRecord job, CancellationToken stoppingToken)
    {
        try
        {
            var current = await jobStore.GetAsync(job.TenantId, job.Id, stoppingToken).ConfigureAwait(false);

            return current?.Status;
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, stoppingToken))
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "Could not read job {JobId} back to label its metric.", job.Id);
            }

            return null;
        }
    }

    /// <summary>
    /// Writes this attempt's outcome to <c>tracon.job.executions</c> and
    /// <c>tracon.job.duration</c>.
    /// </summary>
    /// <param name="job">The job whose attempt finished.</param>
    /// <param name="status">The terminal status reached.</param>
    /// <param name="startedAt">The monotonic timestamp taken when the attempt started.</param>
    /// <param name="handlerKeyTag">
    /// Overrides the handler-key tag. <see langword="null"/> publishes the job's
    /// own key, which is what every path that actually reached a handler does.
    /// </param>
    /// <remarks>
    /// Observability must not break functionality. The job is ALREADY finalized
    /// in the store by the time this runs; a listener callback that throws is
    /// logged and swallowed, never surfaced to the caller. The metric is
    /// written after the store call for the same reason: a job's completion
    /// never depends on a measurement succeeding.
    /// </remarks>
    private void RecordJobMetric(JobRecord job, JobStatus status, long startedAt, string? handlerKeyTag = null)
    {
        if (metrics is null)
        {
            return;
        }

        try
        {
            metrics.RecordJob(
                job.Lane,
                handlerKeyTag ?? job.HandlerKey,
                status,
                job.TenantId,
                _clock.GetElapsedTime(startedAt));
        }
        catch (Exception exception)
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "Could not record the job metric for {JobId}.", job.Id);
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
                    Id = TraconId.NewId(),
                    TenantId = schedule.TenantId,
                    ScheduleId = schedule.Id,
                    HandlerKey = schedule.HandlerKey,
                    Lane = schedule.Lane,
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

    private LeaseRenewal StartLeaseRenewal(
        Guid jobId,
        TimeSpan leaseDuration,
        long leaseRequestedAt,
        CancellationToken stoppingToken)
    {
        // A third, not a half (F-326): at half, the renewal after a missed one
        // lands exactly on the expiry, so one transient store error was enough
        // to let another worker take the job. A third leaves one miss of margin
        // — the same reason SingletonGuard renews at a third (K-743).
        var interval = leaseDuration / 3;

        if (interval <= TimeSpan.Zero)
        {
            interval = TimeSpan.FromSeconds(30);
        }

        // The margin is one renewal interval (F-348): finalization stops
        // retrying that long before the lease this worker can prove runs out,
        // which leaves room for a write still in flight and for clock skew
        // between workers.
        var renewal = new LeaseRenewal(_clock, leaseDuration, interval, leaseRequestedAt, stoppingToken);

        _ = RenewLoopAsync(jobId, leaseDuration, interval, renewal, renewal.Token);

        return renewal;
    }

    private async Task RenewLoopAsync(
        Guid jobId,
        TimeSpan leaseDuration,
        TimeSpan interval,
        LeaseRenewal renewal,
        CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(interval, _clock);

            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                // 🚨 The catch is PER TICK (F-326). One try around the whole loop
                // ended renewal for good on the first transient store error: the
                // handler kept running, the lease ran out, and another worker
                // leased the job and ran it a second time beside the first.
                // IJobHandler promises a lease renewed without limit.
                try
                {
                    var requestedAt = _clock.GetTimestamp();

                    await jobStore.RenewLeaseAsync(jobId, _ownerId, leaseDuration, token).ConfigureAwait(false);

                    renewal.Confirm(requestedAt);
                }
                catch (Exception exception) when (OperationCancellation.IsFailure(exception, token))
                {
                    if (logger is not null && logger.IsEnabled(LogLevel.Warning))
                    {
                        logger.LogWarning(exception, "Lease renewal failed: {JobId}. The next tick tries again.", jobId);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The job finished, or the worker is shutting down. Only then: a
            // store's own timeout is a renewal failure and is logged above (K-840).
        }
    }

    /// <summary>
    /// The renewal of one running job's lease, and how much of that lease
    /// this worker can prove it still holds. Disposing it stops the renewal.
    /// </summary>
    /// <remarks>
    /// The proof is the moment the lease was requested, or the moment the last
    /// successful renewal was sent, plus the lease duration. Both moments are
    /// taken before the store call, so the proof never outlasts the store's
    /// own deadline.
    /// </remarks>
    private sealed class LeaseRenewal : IDisposable
    {
        private readonly TimeProvider _clock;
        private readonly TimeSpan _duration;
        private readonly TimeSpan _margin;
        private readonly CancellationTokenSource _cts;
        private long _confirmedAt;

        public LeaseRenewal(
            TimeProvider clock,
            TimeSpan duration,
            TimeSpan margin,
            long leaseRequestedAt,
            CancellationToken stoppingToken)
        {
            _clock = clock;
            _duration = duration;
            _margin = margin;
            _confirmedAt = leaseRequestedAt;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        }

        /// <summary>Cancelled when the renewal stops. Read it before <see cref="Dispose"/>.</summary>
        public CancellationToken Token => _cts.Token;

        /// <summary>
        /// The time left on the lease this worker can prove, less the margin.
        /// Zero or negative once the margin is reached.
        /// </summary>
        public TimeSpan ProvableRemaining
            => _duration - _margin - _clock.GetElapsedTime(Volatile.Read(ref _confirmedAt));

        /// <summary>Records a renewal that succeeded.</summary>
        /// <param name="requestedAt">The monotonic timestamp taken before the renewal was sent.</param>
        public void Confirm(long requestedAt)
        {
            // A renewal sent after the proven lease ran out proves nothing: the
            // store ignores a renewal from a worker that no longer owns the
            // lease, and reports no error for it.
            if (_clock.GetElapsedTime(Volatile.Read(ref _confirmedAt), requestedAt) < _duration)
            {
                Volatile.Write(ref _confirmedAt, requestedAt);
            }
        }

        /// <summary>Stops the renewal.</summary>
        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }

    /// <summary>
    /// The time a job's finalization may spend trying a store call again,
    /// fixed when the handler returns.
    /// </summary>
    /// <remarks>
    /// Fixed, and not moved by later renewals: a store that keeps renewing the
    /// lease while it refuses the completion would otherwise hold the job
    /// Running, and the worker's slot, without end.
    /// </remarks>
    private sealed class FinalizationWindow(TimeProvider clock, TimeSpan length)
    {
        private readonly long _openedAt = clock.GetTimestamp();

        /// <summary>The time left; zero or negative once the window has closed.</summary>
        public TimeSpan Left => length - clock.GetElapsedTime(_openedAt);
    }

    /// <summary>
    /// Holds the shared concurrency semaphore plus one dedicated semaphore per
    /// lane listed in <see cref="TraconSchedulingOptions.MaxConcurrentJobsPerLane"/>.
    /// </summary>
    /// <remarks>
    /// A dedicated semaphore's size is fixed at the value seen the first time
    /// its lane is requested — the same "fixed for this ExecuteAsync run"
    /// characteristic <see cref="Shared"/> already had before lanes existed.
    /// </remarks>
    private sealed class WorkerSlots(int maxConcurrentJobs) : IDisposable
    {
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _perLane = new(StringComparer.Ordinal);

        public SemaphoreSlim Shared { get; } = new(Math.Max(1, maxConcurrentJobs));

        public SemaphoreSlim ForLane(string lane, int max)
            => _perLane.GetOrAdd(lane, static (_, m) => new SemaphoreSlim(Math.Max(1, m)), max);

        public void Dispose()
        {
            Shared.Dispose();

            foreach (var semaphore in _perLane.Values)
            {
                semaphore.Dispose();
            }
        }
    }
}
