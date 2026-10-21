using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// A background service that closes the human requests whose time has run out
/// (<see cref="WorkflowDefinition.RequestTimeout"/>) and the runs that wait on them.
/// </summary>
/// <remarks>
/// <para>
/// The same pattern as <c>ApprovalExpirationService</c>: only one instance scans across the
/// cluster (<see cref="SingletonGuard"/>), a round closes at most
/// <see cref="TraconWorkflowOptions.MaxRequestExpirationsPerScan"/> rows, and a failed round is
/// logged and tried again on the next tick.
/// </para>
/// <para>
/// <strong>A run closes when its last open request does.</strong> The rows of one run can expire at
/// different times (a request a slow branch published later), so the run is closed only when no
/// row of it is open. It is closed <see cref="RunStatus.Failed"/> with the error type
/// <c>WorkflowRequestExpired</c> when any request it waited on expired unanswered, and
/// <see cref="RunStatus.Completed"/> without an error when every one of them had been answered:
/// an answered run stays <see cref="RunStatus.AwaitingInput"/> and its work continued on
/// another run, so calling it a failure would be wrong. Either way the run stops being
/// <see cref="RunStatus.AwaitingInput"/>, which is what lets retention delete its request event, its
/// checkpoints and its input row.
/// </para>
/// <para>
/// <strong>The row of the run is read before it is closed, and written back whole.</strong>
/// <c>IRunStore.CompleteRunAsync</c> replaces the event count, the usage and the cost with what the
/// completion carries; a completion built from nothing would erase what the run recorded. The
/// service carries them over, and closes only a run that is still
/// <see cref="RunStatus.AwaitingInput"/>.
/// </para>
/// <para>
/// <strong>A run this service fails publishes <c>run.failed</c>, the event the runner publishes</strong>, from the same
/// payload builder (<see cref="WorkflowRunWebhooks"/>): the receiver was told the run waits
/// (<c>workflow.request.pending</c>) and would otherwise never hear that it ended. The event is sent after the run
/// is closed, and a failure to send is logged and changes nothing: the close stands. Delivery is at most once - a
/// process that stops between the close and the send loses the event, as it does in the runner.
/// </para>
/// <para>
/// A run closed <see cref="RunStatus.Completed"/> publishes nothing. That is the ORDINARY end of a run whose request was
/// answered: the answer started a run of its own, which published its own <c>run.completed</c> when it ended, and this
/// run stayed <see cref="RunStatus.AwaitingInput"/> only until its request's time came. Publishing here would send a second
/// <c>run.completed</c> for one production, up to a year after the first.
/// </para>
/// <para>
/// <strong>A process that stops between the two writes is recovered.</strong> A row is closed first
/// and a closed row is never found again, so a stop after the first write would leave the run
/// <see cref="RunStatus.AwaitingInput"/> for good. The instance that takes the lease therefore
/// looks once for such runs (<see cref="IWorkflowRequestStore.ListStrandedAsync"/>) and closes
/// them the same way; it looks again on the next scans while a scan finds a full round of them.
/// </para>
/// </remarks>
internal sealed class WorkflowRequestExpirationService(
    IWorkflowRequestStore requests,
    IRunStore runStore,
    ISingletonLeaseStore leaseStore,
    IOptionsMonitor<TraconWorkflowOptions> optionsMonitor,
    IOptionsMonitor<SingletonExecutionOptions> singletonOptionsMonitor,
    SchemaReadyGate schemaReadyGate,
    TimeProvider? timeProvider = null,
    ILogger<WorkflowRequestExpirationService>? logger = null,
    IRunErrorClassifier? errorClassifier = null,
    IWebhookPublisher? webhookPublisher = null) : BackgroundService
{
    /// <summary>How many scans try to close a run before the service gives up on it.</summary>
    private const int MaxCloseAttempts = 10;

    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    // The runs whose rows are closed but which are not closed themselves yet, with the scans that
    // already tried. Read and written only by the scan, which never overlaps itself.
    private readonly Dictionary<(string TenantId, Guid RunId), int> _unfinished = [];

    // Whether the stranded runs of an earlier process were looked for since this instance took
    // the lease. Set by the scan; cleared by the loop whenever the lease is not held.
    private bool _recovered;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!optionsMonitor.CurrentValue.Enabled)
        {
            return;
        }

        // Wait for the schema to become ready before the first SQL attempt (K-354).
        try
        {
            await schemaReadyGate.WaitAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var guard = new SingletonGuard(leaseStore, singletonOptionsMonitor, "workflow-request-expiration", logger);
        var guardTask = guard.RunAsync(stoppingToken);

        using var timer = new PeriodicTimer(optionsMonitor.CurrentValue.RequestExpirationScanInterval, _clock);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                if (guard.IsHeld)
                {
                    await TickAsync(stoppingToken).ConfigureAwait(false);
                }
                else
                {
                    // Another instance scans now; when this one takes the lease again, the runs the
                    // other left behind are looked for again.
                    _recovered = false;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            await guardTask.ConfigureAwait(false);
        }
    }

    /// <summary>Runs one scan: closes the due rows, then the runs that have no open row left.</summary>
    /// <param name="stoppingToken">The service's stopping token.</param>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// Internal so a test can drive one scan without waiting for the timer. A scan never throws.
    /// </remarks>
    internal async Task TickAsync(CancellationToken stoppingToken)
    {
        var options = optionsMonitor.CurrentValue;
        var now = _clock.GetUtcNow();

        if (!_recovered)
        {
            await FindStrandedRunsAsync(options.MaxRequestExpirationsPerScan, stoppingToken).ConfigureAwait(false);
        }

        try
        {
            var closed = await requests
                .ExpireAsync(now, options.MaxRequestExpirationsPerScan, stoppingToken)
                .ConfigureAwait(false);

            foreach (var run in closed.GroupBy(static row => (row.TenantId!, row.RunId)))
            {
                _unfinished.TryAdd(run.Key, 0);
            }

            if (closed.Count > 0 && logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning("{Count} workflow requests expired and were closed.", closed.Count);
            }
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, stoppingToken))
        {
            // A scan must not terminate the service. The next cycle retries after an error.
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "The workflow request expiration scan failed.");
            }
        }

        // 🚨 The runs are closed AFTER their rows, and a row that is closed is never found again:
        // a run whose close failed (a store that blinked) would stay AwaitingInput for good. The
        // runs that are not closed yet are kept here and tried again on every scan, up to
        // MaxCloseAttempts. The list lives in this process only; a process that stops in between
        // leaves the run to FindStrandedRunsAsync, which the next instance to hold the lease runs.
        foreach (var (key, attempts) in _unfinished.ToArray())
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            if (await CloseRunAsync(key.TenantId, key.RunId, now, stoppingToken).ConfigureAwait(false))
            {
                _unfinished.Remove(key);
            }
            else if (attempts + 1 >= MaxCloseAttempts)
            {
                _unfinished.Remove(key);

                if (logger is not null && logger.IsEnabled(LogLevel.Error))
                {
                    logger.LogError(
                        "Workflow run {RunId} could not be closed after {Attempts} scans for its expired request. It stays AwaitingInput and retention keeps its rows.",
                        key.RunId,
                        MaxCloseAttempts);
                }
            }
            else
            {
                _unfinished[key] = attempts + 1;
            }
        }
    }

    /// <summary>
    /// Adds to the unfinished runs the ones a process that stopped left behind: runs that still
    /// wait although none of their rows is open.
    /// </summary>
    /// <remarks>
    /// Looking is done once per time the lease is taken, and again on the next scans while a look
    /// returns a full round: the runs found are closed by the same scan, so the next look sees the
    /// rest. A failed look is tried again on the next scan.
    /// </remarks>
    private async ValueTask FindStrandedRunsAsync(int max, CancellationToken cancellationToken)
    {
        try
        {
            var stranded = await requests.ListStrandedAsync(max, cancellationToken).ConfigureAwait(false);

            foreach (var run in stranded.GroupBy(static row => (row.TenantId!, row.RunId)))
            {
                _unfinished.TryAdd(run.Key, 0);
            }

            _recovered = stranded.Count < max;

            if (stranded.Count > 0 && logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    "{Count} workflow request rows were closed but their runs still wait; closing the runs.",
                    stranded.Count);
            }
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "The search for workflow runs left waiting after their requests closed failed.");
            }
        }
    }

    /// <summary>
    /// Closes the run if no row of it is open any more.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the run is dealt with (closed, still waiting on a later request,
    /// or no longer waiting); <see langword="false"/> when the store failed and it must be tried again.
    /// </returns>
    private async ValueTask<bool> CloseRunAsync(string tenantId, Guid runId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            // The rows and the run are the tenant's: the store reads them through the ambient tenant.
            using var tenantScope = AmbientTenantScope.Begin(tenantId);

            var rows = await requests.ListByRunAsync(runId, cancellationToken).ConfigureAwait(false);

            if (rows.Any(static row => row.Status == WorkflowRequestStatus.Open))
            {
                // A request published later still waits: the run waits on it.
                return true;
            }

            var run = await runStore.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);

            // Only a run that is still waiting is closed. A run in any other state was closed by
            // something else, and writing over it would replace what that recorded.
            if (run is null ||
                run.Status != RunStatus.AwaitingInput ||
                !string.Equals(run.TenantId, tenantId, StringComparison.Ordinal))
            {
                return true;
            }

            var unanswered = rows
                .Where(static row => row.AnsweredAt is null)
                .OrderBy(static row => row.ExpiresAt)
                .FirstOrDefault();

            var status = unanswered is null ? RunStatus.Completed : RunStatus.Failed;
            var error = unanswered is null ? null : Classify(ExpiredError(unanswered));

            // A step that waits for a tool approval is closed with the run, and BEFORE it: a close that fails
            // here is tried again by the next scan, while the run is still waiting. After the run closed, the
            // scan would find nothing to try again.
            if (error is not null)
            {
                await CloseWaitingStepsAsync(tenantId, runId, now, error, cancellationToken).ConfigureAwait(false);
            }

            await runStore.CompleteRunAsync(
                new RunCompletion
                {
                    RunId = runId,
                    Status = status,
                    CompletedAt = now,
                    EventCount = run.EventCount,
                    Usage = run.Usage,
                    Cost = run.Cost,
                    NonTokenCost = run.NonTokenCost,
                    Error = error,
                    TenantId = tenantId,
                },
                cancellationToken).ConfigureAwait(false);

            // The run is closed: from here the close stands whatever the send does.
            await PublishClosedAsync(tenantId, run, status, error, now).ConfigureAwait(false);

            return true;
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    exception,
                    "Could not close workflow run {RunId} for an expired request; the next scan tries again.",
                    runId);
            }

            return false;
        }
    }

    /// <summary>
    /// Closes the steps of an expired run that still wait for a tool approval, with the error the run closes with.
    /// </summary>
    /// <remarks>
    /// A step whose approval nobody answered would stay <see cref="RunStatus.AwaitingApproval"/> for good, though
    /// the workflow that waited for it has failed. A step whose approval WAS answered is left as it is: the
    /// answer continued on a new run, and an answered run stays in the state it closed with.
    /// <see cref="IRunStore.CompleteRunAsync"/> replaces the event count, the usage and the cost, so the row's own
    /// values are written back.
    /// </remarks>
    private async ValueTask CloseWaitingStepsAsync(
        string tenantId,
        Guid runId,
        DateTimeOffset now,
        RunError error,
        CancellationToken cancellationToken)
    {
        var steps = await runStore
            .QueryRunsAsync(
                new RunQuery { ParentRunId = runId, Status = RunStatus.AwaitingApproval, Take = StepLimit },
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var step in steps)
        {
            await runStore.CompleteRunAsync(
                new RunCompletion
                {
                    RunId = step.Id,
                    Status = RunStatus.Failed,
                    CompletedAt = now,
                    EventCount = step.EventCount,
                    Usage = step.Usage,
                    Cost = step.Cost,
                    NonTokenCost = step.NonTokenCost,
                    Error = error,
                    TenantId = tenantId,
                },
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The most steps of one run that are closed in a scan; a run holds a step for each agent of a pattern.</summary>
    private const int StepLimit = 100;

    /// <summary>Publishes the event a run closed by this service owes its receiver.</summary>
    /// <remarks>
    /// Never throws: a failure is logged. The run row is read again after the close, because the tree's
    /// tokens and cost are computed by the store over the closed row; the runner reads it the same way.
    /// </remarks>
    private async ValueTask PublishClosedAsync(
        string tenantId,
        RunRecord run,
        RunStatus status,
        RunError? error,
        DateTimeOffset now)
    {
        // Only a failure: see the class remarks. The answered run's close is a bookkeeping end, not news.
        if (webhookPublisher is null || status != RunStatus.Failed || WorkflowRunWebhooks.EventFor(status) is not { } eventType)
        {
            return;
        }

        try
        {
            // CancellationToken.None, as the runner's close: a service that is stopping has closed the run
            // and still owes the event, and the publisher only hands it over.
            var record = await runStore.GetRunAsync(run.Id, CancellationToken.None).ConfigureAwait(false);

            await webhookPublisher.PublishAsync(
                tenantId,
                eventType,
                WorkflowRunWebhooks.RunPayload(
                    now,
                    run.Id,
                    run.SessionId ?? string.Empty,
                    run.WorkflowName ?? run.AgentName,
                    status,
                    now - run.StartedAt,
                    record,
                    nonTokenCost: null,
                    error?.Message),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, CancellationToken.None))
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    exception,
                    "The webhook of workflow run {RunId}, closed for an expired request, could not be published.",
                    run.Id);
            }
        }
    }

    private static RunError ExpiredError(WorkflowRequestRecord row)
        => new()
        {
            Type = WorkflowRequestExpiry.ErrorType,
            Message = $"The human request '{row.RequestId}' of workflow '{row.WorkflowName}' was not answered and expired at " +
                      $"{row.ExpiresAt:O}.",
        };

    private RunError Classify(RunError error)
    {
        if (errorClassifier is null)
        {
            return error;
        }

        try
        {
            var classification = errorClassifier.Classify(error);

            return error with { Class = classification.Class, Fingerprint = classification.Fingerprint };
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, CancellationToken.None))
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "The registered IRunErrorClassifier threw while classifying an expired workflow request.");
            }

            return error;
        }
    }
}
