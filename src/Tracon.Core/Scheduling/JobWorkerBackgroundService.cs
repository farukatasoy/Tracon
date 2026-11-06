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
    TraconMetrics? metrics = null,
    IWebhookPublisher? webhookPublisher = null,
    IJobTenantCapSource? tenantCapSource = null) : BackgroundService
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
    private readonly TickOptions<TraconSchedulingOptions> _tickOptions = new(optionsMonitor, logger, "job worker");
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
        try
        {
            await schemaReadyGate.WaitAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.PollInterval, _clock);
        var slots = new JobWorkerSlots(options.MaxConcurrentJobs);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await TickAsync(slots, stoppingToken).ConfigureAwait(false);
                FollowPollInterval(timer);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            // TickAsync leases jobs as concurrent work, and every leased job holds a slot from
            // the moment it is leased: one that waits for its previous attempt to finish is not
            // in _runningJobs yet. The pools therefore say when the work is over, and each one
            // disposes its semaphore with its last returned slot, not before.
            slots.Close();
            await slots.WaitForDrainedAsync().ConfigureAwait(false);
            await WaitForRunningJobsAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Gives the poll timer the interval the options hold now, so a change of <c>PollInterval</c> applies from the
    /// next tick and not from the next start. A value a timer cannot take, or options that cannot be read (a reload
    /// that failed validation), leave the period the timer already has.
    /// </summary>
    /// <param name="timer">The poll timer.</param>
    private void FollowPollInterval(PeriodicTimer timer)
    {
        if (!_tickOptions.TryRead(out var options))
        {
            return;
        }

        var configured = options.PollInterval;

        // Only a change is written: setting the period restarts the wait, and a restart on every tick would
        // push the next tick out by the time the tick itself took.
        if (configured != timer.Period && TimerPeriod.IsValid(configured))
        {
            timer.Period = configured;
        }
    }

    private async Task TickAsync(JobWorkerSlots slots, CancellationToken stoppingToken)
    {
        // While the process is draining, no NEW work starts --
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

        // Read once for the tick: the jobs it leases run on this view as well, so one tick sees one set of options. A tick
        // that cannot read them is skipped, and the loop keeps its timer.
        if (!_tickOptions.TryRead(out var options))
        {
            return;
        }

        // A lane listed in MaxConcurrentJobsPerLane gets its OWN lease pass,
        // scoped to exactly that lane, so a full lane never blocks another
        // lane's slot (the starvation guarantee) and never gets leased
        // beyond its own budget by the shared pass below.
        foreach (var (lane, max) in options.MaxConcurrentJobsPerLane)
        {
            if (options.Lanes is { } subscribed && !subscribed.Contains(lane, StringComparer.Ordinal))
            {
                continue;
            }

            var slotPool = slots.ForLane(lane, max);
            await LeaseLoopAsync(slotPool, [lane], options, stoppingToken).ConfigureAwait(false);
        }

        var sharedLanes = ComputeSharedLanes(options);

        // A non-null, empty list means every subscribed lane already has its
        // own dedicated pass above; there is nothing left for the shared pool.
        if (sharedLanes is null || sharedLanes.Length > 0)
        {
            await LeaseLoopAsync(slots.Shared, sharedLanes, options, stoppingToken).ConfigureAwait(false);
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
        SlotPool slotPool,
        IReadOnlyList<string>? lanes,
        TraconSchedulingOptions options,
        CancellationToken stoppingToken)
    {
        while (await slotPool.TryAcquireAsync(stoppingToken).ConfigureAwait(false))
        {
            JobRecord? job = null;

            // Taken BEFORE the call: the store starts the lease at or after
            // this moment, so the lease this worker counts on never outlasts
            // the store's.
            var leaseRequestedAt = _clock.GetTimestamp();

            try
            {
                job = await jobStore.LeaseAsync(
                    new JobLeaseRequest
                    {
                        Owner = _ownerId,
                        LeaseDuration = options.LeaseDuration,
                        Lanes = lanes,
                        TenantCap = tenantCapSource?.Current,
                    },
                    stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (OperationCancellation.IsFailure(exception, stoppingToken))
            {
                if (logger is not null && logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning(exception, "Job leasing failed.");
                }
            }
            finally
            {
                // The slot goes back on every way out of the lease that did not produce a job: the
                // queue is empty, the store failed, or a stop cancelled the call.
                if (job is null)
                {
                    slotPool.Release();
                }
            }

            if (job is null)
            {
                break;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            if (!_runningJobs.TryAdd(job.Id, completion.Task))
            {
                // A job released for another attempt is leasable at once, so
                // this loop can lease it again while the attempt that released it is
                // still finishing here. The store has already counted the new attempt
                // and set its lease; dropping it left the job Leased until the lease
                // ran out, and then failed it as out of attempts without running the
                // attempt. The new lease waits for the previous attempt instead, and
                // MarkRunningAsync still refuses it if that attempt finished the job.
                _ = RunAfterPreviousAttemptAsync(job, options, leaseRequestedAt, slotPool, completion, stoppingToken);

                continue;
            }

            _ = RunJobAsync(job, options, leaseRequestedAt, slotPool, completion, stoppingToken);
        }
    }

    private async Task RunAfterPreviousAttemptAsync(
        JobRecord job,
        TraconSchedulingOptions options,
        long leaseRequestedAt,
        SlotPool slotPool,
        TaskCompletionSource completion,
        CancellationToken stoppingToken)
    {
        if (logger is not null && logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Job {JobId} was leased for attempt {Attempt} while its previous attempt was still finishing on this worker; it runs once that attempt ends.",
                job.Id,
                job.Attempt);
        }

        // The previous entry's task never faults: RunJobAsync completes it with TrySetResult.
        while (!_runningJobs.TryAdd(job.Id, completion.Task))
        {
            if (_runningJobs.TryGetValue(job.Id, out var previous))
            {
                await previous.ConfigureAwait(false);
            }
        }

        await RunJobAsync(job, options, leaseRequestedAt, slotPool, completion, stoppingToken).ConfigureAwait(false);
    }

    private async Task RunJobAsync(
        JobRecord job,
        TraconSchedulingOptions options,
        long leaseRequestedAt,
        SlotPool slotPool,
        TaskCompletionSource completion,
        CancellationToken stoppingToken)
    {
        try
        {
            await ExecuteJobAsync(job, options, leaseRequestedAt, stoppingToken).ConfigureAwait(false);
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
            slotPool.Release();

            // Removes this attempt's own entry only: a lease that waited for it adds the next one.
            _runningJobs.TryRemove(KeyValuePair.Create(job.Id, completion.Task));
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

    private async Task ExecuteJobAsync(
        JobRecord job,
        TraconSchedulingOptions options,
        long leaseRequestedAt,
        CancellationToken stoppingToken)
    {
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

        // The lease query re-leases a job whose lease ran out and
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
        var closed = await jobStore.CompleteAsync(
            LeaseOf(job),
            new JobCompletion
            {
                Status = JobStatus.Failed,
                CompletedAt = completedAt,
                ErrorMessage = errorMessage,
            },
            stoppingToken).ConfigureAwait(false);

        if (!closed)
        {
            // Not closed: the job was cancelled in the meantime, or its lease went to
            // another worker, which counts and publishes the outcome itself.
            if (await IsCancelledAfterRefusalAsync(job, "fail it", stoppingToken).ConfigureAwait(false))
            {
                RecordJobMetric(job, JobStatus.Cancelled, startedAt, handlerKeyTag);
            }

            return;
        }

        RecordJobMetric(job, JobStatus.Failed, startedAt, handlerKeyTag);
        await PublishJobOutcomeAsync(job, JobStatus.Failed, errorMessage, stoppingToken).ConfigureAwait(false);
    }

    private async Task ExecuteWithHandlerAsync(
        JobRecord job,
        IJobHandler handler,
        TraconSchedulingOptions options,
        long startedAt,
        long leaseRequestedAt,
        CancellationToken stoppingToken)
    {
        if (!await jobStore.MarkRunningAsync(LeaseOf(job), stoppingToken).ConfigureAwait(false))
        {
            // The job left Leased between the lease and here: it was cancelled,
            // or its lease went to another worker. Either way it is not this
            // worker's to run (the result used to be ignored, and a job
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

        // The renewal stays on until the outcome below is written, not only
        // while the handler runs: a store error is retried under a live lease.
        using var leaseRenewal = StartLeaseRenewal(job, options.LeaseDuration, leaseRequestedAt, stoppingToken);

        // The handler's token has FOUR sources, kept apart so the
        // outcome below can say which one fired: the worker stopping, the
        // job cancelled (read every PollInterval by the watch below), the
        // lease lost (the renewal above says so), and the attempt's time limit.
        // Before, only the first existed: a cancel reached the handler only if it
        // asked between items, and nothing bounded a job.
        var maxDuration = ResolveMaxDuration(job, options);
        using var cancelled = new CancellationTokenSource();
        using var deadline = maxDuration is { } limit
            ? new CancellationTokenSource(limit, _clock)
            : new CancellationTokenSource();
        using var handlerToken = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken,
            cancelled.Token,
            deadline.Token,
            leaseRenewal.LostToken);
        var signals = new JobAttemptSignals(maxDuration, stoppingToken, cancelled.Token, deadline.Token, leaseRenewal.LostToken);

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

            // A built-in handler tries a write that belongs to finished
            // work again (a queued run's session) only while this lease holds.
            ProvableLeaseRemaining = () => leaseRenewal.ProvableRemaining,
            Signals = signals,
        };

        Exception? failure = null;

        // Awaited below, never left behind: it cancels a source this method
        // disposes.
        using var watchStop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var watch = WatchForCancellationAsync(job, options.PollInterval, cancelled, watchStop.Token);

        // 🚨 Opened in THIS method's body, around the handler call: an AsyncLocal
        // written in a helper does not flow back to its caller. A workflow run the
        // handler starts reads it when it closes, to name the time limit.
        using (AmbientJobAttempt.Begin(signals))
        {
            // ONLY the handler sits in this try. The store calls that
            // write its outcome used to sit in it too, so a store error after a
            // handler that had succeeded counted as the handler's failure: with
            // attempts left the job was released and the handler ran again (a
            // second model call, repeated tool side effects); on its last attempt
            // the job became Failed while its work had finished. The handler's
            // outcome is decided here; the store calls below only write it.
            try
            {
                await handler.ExecuteAsync(context, handlerToken.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (OperationCancellation.IsFailure(exception, stoppingToken))
            {
                failure = exception;
            }
            finally
            {
                await watchStop.CancelAsync().ConfigureAwait(false);
                await watch.ConfigureAwait(false);
            }
        }

        // Read once, now that the handler has returned: which source fired decides
        // the outcome, not the exception the handler happened to throw.
        var reason = signals.Reason;
        var window = new FinalizationWindow(_clock, leaseRenewal);

        if (reason == JobStopReason.LeaseLost)
        {
            // 🚨 The job is another worker's now, and the log entry written when the lease
            // was lost said why. Nothing is written for this attempt and nothing counted:
            // a Completed or a Pending from here would close or requeue the attempt of the
            // worker that holds the job. That worker counts and publishes the outcome.
            return;
        }

        if (failure is null)
        {
            // A handler that returns normally completes its job, even past its time
            // limit: it finished the work.
            await FinalizeSucceededAsync(job, window, startedAt, stoppingToken).ConfigureAwait(false);
        }
        else if (reason == JobStopReason.Cancelled)
        {
            // The watch found the job cancelled or gone: the store keeps a cancelled
            // job as it is, and a job that is gone has nothing to write.
            RecordJobMetric(job, JobStatus.Cancelled, startedAt);
        }
        else
        {
            // The job may carry its own attempt limit (webhook delivery uses a
            // different ladder than the global setting); if not, the global setting.
            var maxAttempts = job.MaxAttempts ?? options.MaxAttempts;

            await FinalizeFailedAsync(
                job,
                failure,
                maxAttempts,
                window,
                startedAt,
                reason == JobStopReason.MaxDurationExceeded ? maxDuration : null,
                stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Resolves an attempt's time limit: the job's own, else the one its key is mapped
    /// to, else none.
    /// </summary>
    /// <param name="job">The leased job.</param>
    /// <param name="options">The worker's settings.</param>
    /// <returns>The limit; <see langword="null"/> when the job has none.</returns>
    /// <remarks>
    /// The dispatcher and the options validator refuse a value a timer cannot arm. A
    /// job written by another path (another store, a direct write) may still carry one;
    /// arming it would throw here, with the job marked running and no outcome to write.
    /// The bad value is logged and skipped: the limit the key is mapped to applies, as it
    /// does to a job with no limit of its own, and with no mapped limit the job runs
    /// without one, as it would have before the field existed. A bad stored value must
    /// not switch off the operator's limit.
    /// </remarks>
    private TimeSpan? ResolveMaxDuration(JobRecord job, TraconSchedulingOptions options)
    {
        TimeSpan? mapped = options.MaxDurationByHandlerKey.TryGetValue(job.HandlerKey, out var value) && TimerPeriod.IsValid(value)
            ? value
            : null;

        if (job.MaxDuration is not { } own)
        {
            return mapped;
        }

        if (TimerPeriod.IsValid(own))
        {
            return own;
        }

        if (logger is not null && logger.IsEnabled(LogLevel.Error))
        {
            if (mapped is { } fallback)
            {
                logger.LogError(
                    "Job {JobId} carries a time limit of {MaxDuration}, which a timer cannot arm (1 ms to about 49.7 days); it runs under the limit 'Tracon:Scheduling:MaxDurationByHandlerKey' maps its key to ({Mapped}).",
                    job.Id,
                    own,
                    fallback);
            }
            else
            {
                logger.LogError(
                    "Job {JobId} carries a time limit of {MaxDuration}, which a timer cannot arm (1 ms to about 49.7 days); it runs without a limit.",
                    job.Id,
                    own);
            }
        }

        return mapped;
    }

    /// <summary>
    /// Reads a running job's status every <paramref name="interval"/> and cancels
    /// <paramref name="cancelled"/> when it finds the job cancelled or gone.
    /// </summary>
    /// <param name="job">The running job.</param>
    /// <param name="interval">The worker's poll interval.</param>
    /// <param name="cancelled">The source behind the handler's token that a cancel cancels.</param>
    /// <param name="token">Cancelled when the handler has returned, or the worker stops.</param>
    /// <returns>A task that completes when the watch ends; it never throws.</returns>
    /// <remarks>
    /// <para>
    /// The cancel endpoint only writes the store, and the lease renewal does not read
    /// the status, so without this a cancel reached a running handler only if the
    /// handler asked (<see cref="JobContext.IsCancelledAsync"/>), between items. One
    /// read per running job per <c>PollInterval</c>: the same interval the worker
    /// already polls for work, ten seconds by default.
    /// </para>
    /// <para>
    /// A store error is caught PER TICK, as the renewal does: one failed read
    /// must not end the watch, and must not touch the handler either.
    /// </para>
    /// </remarks>
    private async Task WatchForCancellationAsync(
        JobRecord job,
        TimeSpan interval,
        CancellationTokenSource cancelled,
        CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(interval, _clock);

            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                try
                {
                    var current = await jobStore.GetAsync(job.TenantId, job.Id, token).ConfigureAwait(false);

                    if (current is null || current.Status == JobStatus.Cancelled)
                    {
                        await cancelled.CancelAsync().ConfigureAwait(false);

                        return;
                    }
                }
                catch (Exception exception) when (OperationCancellation.IsFailure(exception, token))
                {
                    if (logger is not null && logger.IsEnabled(LogLevel.Warning))
                    {
                        logger.LogWarning(exception, "Could not read job {JobId} to see whether it was cancelled. The next tick tries again.", job.Id);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The handler returned, or the worker is stopping. Only then: a store's
            // own timeout is a failed read and is logged above.
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
            async ct =>
            {
                finalItems = await jobStore.ListItemsAsync(job.Id, ct).ConfigureAwait(false);

                return true;
            },
            appliedStatus: null,
            window,
            stoppingToken).ConfigureAwait(false);

        if (listed != FinalizationResult.Written)
        {
            return;
        }

        var allFailed = finalItems.Count > 0 && finalItems.All(item => item.Status == JobItemStatus.Failed);

        var completion = new JobCompletion
        {
            Status = allFailed ? JobStatus.Failed : JobStatus.Completed,
            CompletedAt = _clock.GetUtcNow(),
            ErrorMessage = allFailed ? "All of the job's items failed." : null,
        };

        var lease = LeaseOf(job);

        var completed = await RetryFinalizationAsync(
            job,
            "complete it",
            ct => jobStore.CompleteAsync(lease, completion, ct),
            completion.Status,
            window,
            stoppingToken).ConfigureAwait(false);

        if (completed == FinalizationResult.GaveUp)
        {
            return;
        }

        var finalStatus = completion.Status;

        if (completed == FinalizationResult.Refused)
        {
            // A job cancelled while its handler ran (the handler saw it through
            // IsCancelledAsync and exited early) is not overwritten: the store
            // refuses the write on a terminal job. Cancelled IS terminal, so the
            // attempt is still counted. A refusal for any other reason is a lost
            // lease: the job is another worker's, and that worker counts it.
            if (!await IsCancelledAfterRefusalAsync(job, "complete it", stoppingToken).ConfigureAwait(false))
            {
                return;
            }

            finalStatus = JobStatus.Cancelled;
        }

        window.Settled();

        RecordJobMetric(job, finalStatus, startedAt);
        await PublishJobOutcomeAsync(job, finalStatus, completion.ErrorMessage, stoppingToken).ConfigureAwait(false);
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
    /// <param name="maxDurationExceeded">
    /// The time limit the attempt reached, when that is why the handler's token was
    /// cancelled; <see langword="null"/> otherwise.
    /// </param>
    /// <param name="stoppingToken">The worker's shutdown token.</param>
    /// <remarks>
    /// <para>
    /// A <see cref="NonRetryableJobException"/> fails the job on this attempt,
    /// whatever attempts are left: the handler's work is done, and running it
    /// again would repeat it. Its message already carries a stable code
    /// and the reference of the one log entry its thrower wrote, so it is
    /// persisted as is and not logged a second time.
    /// </para>
    /// <para>
    /// A failure <see cref="FinalFailure.IsFinal"/> calls final fails the job on
    /// this attempt too: the next attempt would get the same answer and pay for the
    /// work again. It is logged and persisted like any other failure. A
    /// <see cref="JobRetryException"/> is the handler asking for another attempt,
    /// and it gets one.
    /// </para>
    /// <para>
    /// An attempt that reached its time limit fails with
    /// <see cref="JobErrorCodes.MaxDurationExceeded"/>, whatever the handler threw on
    /// its way out: the exception is the handler noticing its token, not the cause. It
    /// counts like any failed attempt.
    /// </para>
    /// </remarks>
    // A timed-out attempt is a failed attempt, leased again while attempts are left.
    private async Task FinalizeFailedAsync(
        JobRecord job,
        Exception exception,
        int maxAttempts,
        FinalizationWindow window,
        long startedAt,
        TimeSpan? maxDurationExceeded,
        CancellationToken stoppingToken)
    {
        var nonRetryable = exception is NonRetryableJobException;

        // Read from the failure's stable identity, never its text. An attempt
        // that reached its time limit is not judged by what the handler threw on its
        // way out (see the remarks).
        var final = nonRetryable
            || (maxDurationExceeded is null && exception is not JobRetryException && FinalFailure.IsFinal(exception));
        var correlationId = SafeErrorText.NewCorrelationId();
        string safeMessage;

        if (!nonRetryable && maxDurationExceeded is { } limit)
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(
                    exception,
                    "Job {JobId} reached its time limit of {MaxDuration} on attempt {Attempt}; its handler was cancelled. (ref: {CorrelationId})",
                    job.Id,
                    limit,
                    job.Attempt,
                    correlationId);
            }

            safeMessage = JobErrorCodes.Format(JobErrorCodes.MaxDurationExceeded, correlationId);
        }
        else
        {
            if (!nonRetryable && logger is not null && logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(exception, "Job {JobId} failed. (ref: {CorrelationId})", job.Id, correlationId);
            }

            // A NonRetryableJobException is a TraconException, so its message (the
            // code and its own reference) is what this produces.
            safeMessage = SafeErrorText.ForPersistence(exception, correlationId);
        }

        // 🚨 Cancelled is terminal. A handler that saw the cancel and
        // threw on its way out used to be treated as a plain failure: with
        // attempts left the job went back to Pending and ran again, on the
        // last attempt it became Failed. The store refuses both writes on a
        // terminal job too; this read is what keeps the metric honest, so a
        // read that fails does not hold back the write below.
        if (await TryReadStatusAsync(job, stoppingToken).ConfigureAwait(false) == JobStatus.Cancelled)
        {
            RecordJobMetric(job, JobStatus.Cancelled, startedAt);

            return;
        }

        if (final || job.Attempt >= maxAttempts)
        {
            var completion = new JobCompletion
            {
                Status = JobStatus.Failed,
                CompletedAt = _clock.GetUtcNow(),
                ErrorMessage = safeMessage,
            };

            var failLease = LeaseOf(job);

            var failed = await RetryFinalizationAsync(
                job,
                "fail it",
                ct => jobStore.CompleteAsync(failLease, completion, ct),
                JobStatus.Failed,
                window,
                stoppingToken).ConfigureAwait(false);

            if (failed == FinalizationResult.Written)
            {
                window.Settled();

                RecordJobMetric(job, JobStatus.Failed, startedAt);
                await PublishJobOutcomeAsync(job, JobStatus.Failed, safeMessage, stoppingToken).ConfigureAwait(false);
            }
            else if (failed == FinalizationResult.Refused
                && await IsCancelledAfterRefusalAsync(job, "fail it", stoppingToken).ConfigureAwait(false))
            {
                // Cancelled between the read above and this write.
                RecordJobMetric(job, JobStatus.Cancelled, startedAt);
            }

            return;
        }

        // The handler may request a delay; if not, the old behavior
        // (immediately re-leasable) is preserved. What a handler asks for is a request, and a store refuses a call whose
        // delay is outside its range: the worker brings the request into range and says so, instead of handing the store a
        // value it throws on, which would leave the job Running until its lease ran out.
        var requestedRetryAfter = (exception as JobRetryException)?.RetryAfter;
        var retryAfter = LeaseTimeRange.ClampRetryDelay(requestedRetryAfter, out var retryAfterChanged);

        if (retryAfterChanged && logger is not null && logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning(
                "Job {JobId} attempt {Attempt}: the handler asked to retry after {Requested}, which is outside the range the job store accepts (zero to about 68 years); the job is retried after {Used} instead.",
                job.Id,
                job.Attempt,
                requestedRetryAfter,
                retryAfter ?? TimeSpan.Zero);
        }

        var releaseLease = LeaseOf(job);

        var released = await RetryFinalizationAsync(
            job,
            "release it for another attempt",
            ct => jobStore.ReleaseForRetryAsync(releaseLease, safeMessage, retryAfter, ct),
            JobStatus.Pending,
            window,
            stoppingToken).ConfigureAwait(false);

        if (released == FinalizationResult.Written)
        {
            window.Settled();
        }
        else if (released == FinalizationResult.Refused
            && await IsCancelledAfterRefusalAsync(job, "release it for another attempt", stoppingToken).ConfigureAwait(false))
        {
            // Cancelled between the read above and this write.
            RecordJobMetric(job, JobStatus.Cancelled, startedAt);
        }

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
    /// <param name="appliedStatus">
    /// The status the call leaves the job in when it applies; <see langword="null"/> for a call that
    /// writes nothing. It lets a try that follows an error recognise a write the earlier try had already
    /// made.
    /// </param>
    /// <param name="window">How long the tries may go on.</param>
    /// <param name="stoppingToken">The worker's shutdown token.</param>
    /// <returns>
    /// <see cref="FinalizationResult.Written"/> once the call applied;
    /// <see cref="FinalizationResult.Refused"/> when the store answered that the write
    /// did not apply (the lease is another attempt's, or the job is closed), or when this
    /// worker already knows the lease is lost; <see cref="FinalizationResult.GaveUp"/>
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
    /// A refusal is an answer, not an error, and is not tried again: the store
    /// fences the completion and release calls by the lease, so the same call would be
    /// refused again.
    /// </para>
    /// <para>
    /// The tries stop at the window's end even though the store fences the write: once
    /// the lease this worker can prove has run out, the job may belong to another worker
    /// already, and a write that is sent anyway only has the fence to stop it. The window
    /// closes at once when the renewal reports the lease lost. Shutdown ends the tries
    /// too; its cancellation propagates like that of any other store call.
    /// </para>
    /// </remarks>
    private async Task<FinalizationResult> RetryFinalizationAsync(
        JobRecord job,
        string step,
        Func<CancellationToken, ValueTask<bool>> call,
        JobStatus? appliedStatus,
        FinalizationWindow window,
        CancellationToken stoppingToken)
    {
        var delay = FinalizationRetryFirstDelay;
        var failedBefore = false;

        while (true)
        {
            if (window.LeaseLost)
            {
                return FinalizationResult.Refused;
            }

            try
            {
                if (await call(stoppingToken).ConfigureAwait(false))
                {
                    return FinalizationResult.Written;
                }

                // 🚨 A refusal after an error is not yet a lost lease: the earlier try may have
                // committed and only its answer been lost (a dropped connection). The second try
                // then finds the job already closed, and the store answers false because the
                // lease is gone, which a write that applied also clears.
                return failedBefore
                    && appliedStatus is { } expected
                    && await AppliedByThisAttemptAsync(job, expected, stoppingToken).ConfigureAwait(false)
                        ? FinalizationResult.Written
                        : FinalizationResult.Refused;
            }
            catch (Exception exception) when (OperationCancellation.IsFailure(exception, stoppingToken))
            {
                failedBefore = true;

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

                    return FinalizationResult.GaveUp;
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
    /// Tells whether a job is in the status this attempt's own write leaves it in, on this attempt.
    /// </summary>
    /// <param name="job">The leased job.</param>
    /// <param name="expected">The status the write leaves the job in.</param>
    /// <param name="stoppingToken">The worker's shutdown token.</param>
    /// <returns>
    /// <see langword="true"/> when the job carries this attempt's number and that status; <see langword="false"/> when
    /// it does not, or the read failed.
    /// </returns>
    /// <remarks>
    /// The attempt number is what makes the answer this attempt's: it grows each time the job is leased and the
    /// store lets only the holder of that attempt close or requeue it, so no other writer leaves the job in a
    /// terminal status (or back in the queue) with this attempt's number. A cancelled job reads
    /// <see cref="JobStatus.Cancelled"/> and never matches.
    /// </remarks>
    private async Task<bool> AppliedByThisAttemptAsync(JobRecord job, JobStatus expected, CancellationToken stoppingToken)
    {
        try
        {
            var current = await jobStore.GetAsync(job.TenantId, job.Id, stoppingToken).ConfigureAwait(false);

            return current is not null && current.Attempt == job.Attempt && current.Status == expected;
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, stoppingToken))
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "Could not read job {JobId} back to learn whether an earlier try wrote its outcome.", job.Id);
            }

            return false;
        }
    }

    /// <summary>
    /// Tells why a write that needs the lease was refused: the job was cancelled in the
    /// meantime, or the lease is gone.
    /// </summary>
    /// <param name="job">The leased job.</param>
    /// <param name="step">What the refused write did, for the log.</param>
    /// <param name="stoppingToken">The worker's shutdown token.</param>
    /// <returns>
    /// <see langword="true"/> when the job is cancelled, which is counted like any cancelled
    /// job; <see langword="false"/> when the lease was lost, which the log records and nothing
    /// else follows from: the job belongs to another attempt, and that attempt counts it.
    /// </returns>
    /// <remarks>
    /// Cancelling a job clears its lease, so a cancelled job refuses a write exactly as a
    /// job that changed hands does. Only a read tells the two apart. A read that fails
    /// answers "lost": the write did not apply either way, and the worker must not count a
    /// job it cannot show it still owns.
    /// </remarks>
    private async Task<bool> IsCancelledAfterRefusalAsync(JobRecord job, string step, CancellationToken stoppingToken)
    {
        if (await TryReadStatusAsync(job, stoppingToken).ConfigureAwait(false) == JobStatus.Cancelled)
        {
            return true;
        }

        LogLeaseLost(job, $"the store refused to {step}");

        return false;
    }

    private void LogLeaseLost(JobRecord job, string why)
    {
        if (logger is not null && logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning(
                "Job {JobId} attempt {Attempt}: this worker no longer holds the lease ({Reason}); the attempt's result is not written.",
                job.Id,
                job.Attempt,
                why);
        }
    }

    /// <summary>Builds the lease this worker holds on <paramref name="job"/>, a record <c>LeaseAsync</c> returned to it.</summary>
    /// <param name="job">The leased job.</param>
    /// <returns>The lease: the job, this worker, and the attempt the lease counted.</returns>
    /// <remarks>
    /// Built from the worker's own identity, not from <see cref="JobRecord.LeaseOwner"/>: the worker
    /// knows who it leased as, whether or not a store fills that field on the record it returns.
    /// </remarks>
    private JobLease LeaseOf(JobRecord job)
        => new() { JobId = job.Id, Owner = _ownerId, Attempt = job.Attempt };

    /// <summary>
    /// Publishes <c>job.completed</c> or <c>job.failed</c> for a job that just
    /// reached that status.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A cancelled job publishes nothing, the same as a cancelled run. A
    /// webhook delivery job publishes nothing either: its own event would queue
    /// another delivery job for it, without end.
    /// </para>
    /// <para>
    /// The item counts come from a fresh read of the job; when that read fails
    /// the leased copy's counts are sent. A failure here is logged and changes
    /// nothing about the job (observability must not break functionality).
    /// </para>
    /// </remarks>
    // The job.* events were documented and accepted on subscribe, but no
    // code published them.
    private async Task PublishJobOutcomeAsync(JobRecord job, JobStatus status, string? errorMessage, CancellationToken stoppingToken)
    {
        if (webhookPublisher is null ||
            status is not (JobStatus.Completed or JobStatus.Failed) ||
            string.Equals(job.HandlerKey, JobHandlerKeys.WebhookDelivery, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            JobRecord latest;

            try
            {
                latest = await jobStore.GetAsync(job.TenantId, job.Id, stoppingToken).ConfigureAwait(false) ?? job;
            }
            catch (Exception exception) when (OperationCancellation.IsFailure(exception, stoppingToken))
            {
                latest = job;
            }

            await webhookPublisher.PublishAsync(
                job.TenantId,
                status == JobStatus.Completed ? WebhookEvents.JobCompleted : WebhookEvents.JobFailed,
                new WebhookEventPayload
                {
                    OccurredAt = _clock.GetUtcNow(),
                    Job = new WebhookJobSummary
                    {
                        JobId = job.Id.ToString(),
                        Kind = job.HandlerKey,
                        TargetName = job.TargetName,
                        Status = status.ToString(),
                        TotalItems = latest.TotalItems,
                        DoneItems = latest.DoneItems,
                        FailedItems = latest.FailedItems,
                        Error = errorMessage,
                    },
                },
                stoppingToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, stoppingToken))
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "The webhook of job {JobId} could not be published.", job.Id);
            }
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
            // 🚨 The catch is PER ROW. The due list spans every tenant and a store gives it in no particular
            // order, so one row the worker cannot advance, with the failure thrown out of the loop, kept every row
            // behind it from dispatching on every tick: another tenant's schedules stopped for as long as the row
            // stayed. A row that fails costs that row its tick and nothing else.
            try
            {
                await DispatchScheduleAsync(schedule, now, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (OperationCancellation.IsFailure(exception, stoppingToken))
            {
                if (logger is not null && logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning(exception, "Schedule '{Name}' failed to dispatch; the other due schedules are unaffected.", schedule.Name);
                }
            }
        }
    }

    private async Task DispatchScheduleAsync(JobSchedule schedule, DateTimeOffset now, CancellationToken stoppingToken)
    {
        if (schedule.NextRunAt is not { } nextRunAt || schedule.Cron is not { Length: > 0 } cron)
        {
            return;
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

            return;
        }

        // Another instance may have claimed the same schedule at the same
        // time; the job is only created if this instance's claim wins.
        var claimed = await scheduleStore
            .TryClaimNextRunAsync(schedule.Id, nextRunAt, newNextRunAt, now, stoppingToken)
            .ConfigureAwait(false);

        if (!claimed)
        {
            return;
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
            // The schedule's payload is copied as written; an attribution
            // envelope in it travels to the handler with the job.
            JobPayload.Read(schedule.HandlerKey, schedule.Payload).Items,
            stoppingToken).ConfigureAwait(false);
    }

    private LeaseRenewal StartLeaseRenewal(
        JobRecord job,
        TimeSpan leaseDuration,
        long leaseRequestedAt,
        CancellationToken stoppingToken)
    {
        // A third, not a half: at half, the renewal after a missed one
        // lands exactly on the expiry, so one transient store error was enough
        // to let another worker take the job. A third leaves one miss of margin
        // — the same reason SingletonGuard renews at a third.
        var interval = leaseDuration / 3;

        if (interval <= TimeSpan.Zero)
        {
            interval = TimeSpan.FromSeconds(30);
        }

        // The margin is one renewal interval: finalization stops
        // retrying that long before the lease this worker can prove runs out,
        // which leaves room for a write still in flight and for clock skew
        // between workers.
        var renewal = new LeaseRenewal(_clock, leaseDuration, interval, leaseRequestedAt, stoppingToken);

        _ = RenewLoopAsync(job, leaseDuration, interval, renewal, renewal.Token);

        return renewal;
    }

    private async Task RenewLoopAsync(
        JobRecord job,
        TimeSpan leaseDuration,
        TimeSpan interval,
        LeaseRenewal renewal,
        CancellationToken token)
    {
        var lease = LeaseOf(job);

        try
        {
            using var timer = new PeriodicTimer(interval, _clock);

            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                // 🚨 The catch is PER TICK. One try around the whole loop
                // ended renewal for good on the first transient store error: the
                // handler kept running, the lease ran out, and another worker
                // leased the job and ran it a second time beside the first.
                // A store ERROR is a missed tick; only an ANSWER (the store says the
                // lease is not this worker's) or a lease that can no longer be proven
                // ends the renewal, and then the handler is told.
                try
                {
                    var requestedAt = _clock.GetTimestamp();

                    switch (await RenewWithinAsync(job, lease, leaseDuration, renewal.CallBudget, token).ConfigureAwait(false))
                    {
                        case RenewalOutcome.Renewed:
                            renewal.Confirm(requestedAt);

                            continue;

                        case RenewalOutcome.Unanswered:
                            await LoseLeaseAsync(job, renewal, "a renewal did not answer before the lease could no longer be proven").ConfigureAwait(false);

                            return;

                        default:
                            // The attempt may have ended while the answer was on its way: a job this worker closed answers
                            // "refused" too, and that is not a lease lost.
                            if (token.IsCancellationRequested)
                            {
                                return;
                            }

                            await StopRefusedRenewalAsync(job, renewal, token).ConfigureAwait(false);

                            return;
                    }
                }
                catch (Exception exception) when (OperationCancellation.IsFailure(exception, token))
                {
                    if (logger is not null && logger.IsEnabled(LogLevel.Warning))
                    {
                        logger.LogWarning(exception, "Lease renewal failed: {JobId}. The next tick tries again.", job.Id);
                    }

                    // Errors cannot go on for ever: past the point the lease can be proven
                    // the job may be another worker's, and this one cannot tell.
                    if (renewal.ProvableRemaining <= TimeSpan.Zero)
                    {
                        await LoseLeaseAsync(job, renewal, "its renewals kept failing until the lease could no longer be proven").ConfigureAwait(false);

                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The job finished, or the worker is shutting down. Only then: a
            // store's own timeout is a renewal failure and is logged above.
        }
    }

    /// <summary>How one renewal call came out.</summary>
    private enum RenewalOutcome
    {
        /// <summary>The store extended the lease.</summary>
        Renewed,

        /// <summary>The store answered that the lease is not this worker's (or the job is closed).</summary>
        Refused,

        /// <summary>The store did not answer within the time the lease could still be proven.</summary>
        Unanswered,
    }

    /// <summary>Sends one renewal and waits for the answer no longer than <paramref name="budget"/>.</summary>
    /// <param name="job">The leased job.</param>
    /// <param name="lease">The lease to renew.</param>
    /// <param name="leaseDuration">The duration to renew it for.</param>
    /// <param name="budget">How long the answer may take: what is left of the lease this worker can prove.</param>
    /// <param name="token">The renewal's token.</param>
    /// <returns>What the store answered, or <see cref="RenewalOutcome.Unanswered"/> when it did not in time.</returns>
    /// <remarks>
    /// <para>
    /// A call that never returns used to hold the loop for good: the lease ran out under a handler that was never told. The
    /// wait is bounded and the call is let go of when the time is up. <see cref="Task.WaitAsync(TimeSpan, TimeProvider, CancellationToken)"/>
    /// ends the wait but not the call, so the call gets its own token to be cancelled (a store that honors it stops) and
    /// is looked at until it ends, or what it ends with would surface later as an unobserved exception.
    /// </para>
    /// <para>
    /// An answer that comes after the time is up is not read: the loop has given the lease up by then, and what the store
    /// says about a lease the worker can no longer prove does not give it back.
    /// </para>
    /// </remarks>
    private async Task<RenewalOutcome> RenewWithinAsync(
        JobRecord job,
        JobLease lease,
        TimeSpan leaseDuration,
        TimeSpan budget,
        CancellationToken token)
    {
        var callSource = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<bool> call;

        try
        {
            call = jobStore.RenewLeaseAsync(lease, leaseDuration, callSource.Token).AsTask();
        }
        catch
        {
            callSource.Dispose();

            throw;
        }

        try
        {
            return await call.WaitAsync(budget, _clock, token).ConfigureAwait(false)
                ? RenewalOutcome.Renewed
                : RenewalOutcome.Refused;
        }
        catch (TimeoutException) when (!call.IsCompleted)
        {
            // A store that throws a TimeoutException of its own has completed the call: that one is a failed renewal.
            return RenewalOutcome.Unanswered;
        }
        finally
        {
            if (call.IsCompleted)
            {
                callSource.Dispose();
            }
            else
            {
                _ = AbandonRenewalAsync(job, call, callSource);
            }
        }
    }

    /// <summary>
    /// Cancels a renewal call the loop will not wait for any longer, and looks at how it ends.
    /// </summary>
    /// <param name="job">The leased job.</param>
    /// <param name="call">The call that has not ended.</param>
    /// <param name="callSource">The call's token source; disposed once the call has ended, not before, because the call may still read its token.</param>
    /// <returns>A task that ends with the call and never faults.</returns>
    private async Task AbandonRenewalAsync(JobRecord job, Task<bool> call, CancellationTokenSource callSource)
    {
        // The token that governs the call, read while the source is alive.
        var callToken = callSource.Token;

        try
        {
            try
            {
                await callSource.CancelAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (OperationCancellation.IsFailure(exception, callToken))
            {
                // A callback the store put on the token threw. The loop has nothing to do with it.
                LogAbandonedRenewal(job, exception);
            }

            await call.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (callToken.IsCancellationRequested)
        {
            // The call honored the token the loop cancelled.
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, callToken))
        {
            // Nobody is waiting for the answer any more; looking at the failure keeps it from surfacing later as an
            // unobserved one.
            LogAbandonedRenewal(job, exception);
        }
        finally
        {
            callSource.Dispose();
        }
    }

    private void LogAbandonedRenewal(JobRecord job, Exception exception)
    {
        if (logger is not null && logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(exception, "The renewal call of job {JobId} that the worker gave up on ended with an error.", job.Id);
        }
    }

    /// <summary>
    /// Gives the lease up: says why, and tells the handler.
    /// </summary>
    /// <param name="job">The leased job.</param>
    /// <param name="renewal">The attempt's renewal.</param>
    /// <param name="why">Why the lease can no longer be counted on.</param>
    /// <returns>A task that completes when the handler's token has been cancelled.</returns>
    private async Task LoseLeaseAsync(JobRecord job, LeaseRenewal renewal, string why)
    {
        LogLeaseLost(job, why);

        if (await renewal.MarkLostAsync().ConfigureAwait(false) is { } failure
            && logger is not null
            && logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning(
                failure,
                "Job {JobId}: a callback on the handler's token threw while the lease loss was announced; the handler is stopped all the same.",
                job.Id);
        }
    }

    /// <summary>
    /// Handles a renewal the store refused: ends the renewal, and tells the handler when the
    /// job has changed hands.
    /// </summary>
    /// <param name="job">The leased job.</param>
    /// <param name="renewal">The attempt's renewal.</param>
    /// <param name="token">The renewal's token.</param>
    /// <returns>A task that completes when the refusal has been handled.</returns>
    /// <remarks>
    /// Cancelling a job clears its lease, so a refusal may only mean the job was cancelled. That case
    /// is the cancel watch's: it reads the status itself, stops the handler as a cancel and counts the
    /// job as cancelled, and a handler told that its lease was lost would leave a cancelled job's
    /// checkpoints behind. A read that throws propagates to the loop, which counts it as a missed
    /// tick and asks again on the next one. A read that comes back after the attempt ended finds the job
    /// this worker closed, and says nothing about a lease.
    /// </remarks>
    private async Task StopRefusedRenewalAsync(JobRecord job, LeaseRenewal renewal, CancellationToken token)
    {
        var current = await jobStore.GetAsync(job.TenantId, job.Id, token).ConfigureAwait(false);

        if (current is null || current.Status == JobStatus.Cancelled || token.IsCancellationRequested)
        {
            return;
        }

        await LoseLeaseAsync(job, renewal, "the store refused the renewal").ConfigureAwait(false);
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
        private readonly CancellationTokenSource _lost = new();
        private long _confirmedAt;
        private int _isLost;

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

        /// <summary>Cancelled when the lease is lost. Read it before <see cref="Dispose"/>.</summary>
        public CancellationToken LostToken => _lost.Token;

        /// <summary>Gets whether the lease was lost: the store refused a renewal, or none could be proven in time.</summary>
        public bool IsLost => Volatile.Read(ref _isLost) != 0;

        /// <summary>
        /// The time left on the lease this worker can prove, less the margin.
        /// Zero or negative once the margin is reached.
        /// </summary>
        public TimeSpan ProvableRemaining
            => _duration - _margin - _clock.GetElapsedTime(Volatile.Read(ref _confirmedAt));

        /// <summary>
        /// How long a renewal sent now may go unanswered before the lease is given up: the time left on the proven lease,
        /// less the margin, and never less than half a margin.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A tick that comes on schedule leaves a full margin to prove (the last confirmation was one interval ago), so
        /// the renewal is given up at the point the proof ends. A tick that comes after a missed one finds that point
        /// reached, and a store that is healthy must still be able to answer: it gets half a margin, which leaves the other
        /// half as the time to stop in before the lease runs out. The budget is one continuous function of the time
        /// left, so a renewal that is a few milliseconds earlier or later is given the same time within a few
        /// milliseconds. It is never zero or negative, and not "lost at once" when the proof has run out: a store that
        /// answers the next renewal still tells who holds the lease, and a worker whose clock jumped past the lease on a
        /// healthy store finishes its job (see <c>JobLeaseClockStepTests</c>).
        /// </para>
        /// </remarks>
        public TimeSpan CallBudget
            => TimeSpan.FromTicks(Math.Max(ProvableRemaining.Ticks, _margin.Ticks / 2));

        /// <summary>Records a renewal that succeeded.</summary>
        /// <param name="requestedAt">The monotonic timestamp taken before the renewal was sent.</param>
        public void Confirm(long requestedAt)
        {
            // A renewal sent after the proven lease ran out proves nothing: the job
            // may have changed hands in between, and an answer that only says the
            // lease was extended says nothing about who holds it now.
            if (_clock.GetElapsedTime(Volatile.Read(ref _confirmedAt), requestedAt) < _duration)
            {
                Volatile.Write(ref _confirmedAt, requestedAt);
            }
        }

        /// <summary>Records that the lease is lost and cancels <see cref="LostToken"/>.</summary>
        /// <returns>
        /// What a callback registered on the token threw while the token was cancelled, or <see langword="null"/>. The
        /// loss is recorded and the token is cancelled whatever the callbacks do.
        /// </returns>
        /// <remarks>
        /// The handler's token is linked to this one, and a token that is cancelled runs the callbacks registered on it on
        /// the thread that cancelled it. A callback of the handler's that throws must not leave the renewal loop with the
        /// exception: the loop runs as a task nobody awaits, and the failure would be lost.
        /// </remarks>
        public async ValueTask<Exception?> MarkLostAsync()
        {
            Volatile.Write(ref _isLost, 1);

            try
            {
                await _lost.CancelAsync().ConfigureAwait(false);

                return null;
            }
            catch (ObjectDisposedException)
            {
                // The attempt ended while the loop was answering: nobody is left to tell.
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        /// <summary>Stops the renewal once the attempt's outcome is written. Safe to call more than once.</summary>
        public void Stop()
        {
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed: the renewal has stopped.
            }
        }

        /// <summary>Stops the renewal.</summary>
        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
            _lost.Dispose();
        }
    }

    /// <summary>
    /// The time a job's finalization may spend trying a store call again,
    /// fixed when the handler returns.
    /// </summary>
    /// <remarks>
    /// Fixed, and not moved by later renewals: a store that keeps renewing the
    /// lease while it refuses the completion would otherwise hold the job
    /// Running, and the worker's slot, without end. It closes early when the
    /// renewal reports the lease lost.
    /// </remarks>
    private sealed class FinalizationWindow(TimeProvider clock, LeaseRenewal renewal)
    {
        private readonly long _openedAt = clock.GetTimestamp();
        private TimeSpan Length { get; } = renewal.ProvableRemaining;

        /// <summary>Gets whether the lease was lost: nothing is written for this attempt.</summary>
        public bool LeaseLost => renewal.IsLost;

        /// <summary>The time left; zero or negative once the window has closed.</summary>
        public TimeSpan Left => renewal.IsLost ? TimeSpan.Zero : Length - clock.GetElapsedTime(_openedAt);

        /// <summary>
        /// Says the attempt's outcome is written, so the lease no longer needs renewing. A renewal that
        /// went on would be refused (the job is closed or requeued) and read as a lost lease.
        /// </summary>
        public void Settled() => renewal.Stop();
    }

    /// <summary>What one fenced finalization step came to.</summary>
    private enum FinalizationResult
    {
        /// <summary>The store applied the write.</summary>
        Written,

        /// <summary>The store answered that the write did not apply, or the lease was already known to be lost.</summary>
        Refused,

        /// <summary>Store errors used up the window.</summary>
        GaveUp,
    }
}
