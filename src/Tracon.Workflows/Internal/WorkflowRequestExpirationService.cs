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
    IRunErrorClassifier? errorClassifier = null) : BackgroundService
{
    /// <summary>How many scans try to close a run before the service gives up on it.</summary>
    private const int MaxCloseAttempts = 10;

    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    // The runs whose rows are closed but which are not closed themselves yet, with the scans that
    // already tried. Read and written only by the scan, which never overlaps itself.
    private readonly Dictionary<(string TenantId, Guid RunId), int> _unfinished = [];

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
        // MaxCloseAttempts. They are kept in this process only; a process that stops in between
        // leaves the run waiting, and retention keeps its rows like before the limit existed.
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

            await runStore.CompleteRunAsync(
                new RunCompletion
                {
                    RunId = runId,
                    Status = unanswered is null ? RunStatus.Completed : RunStatus.Failed,
                    CompletedAt = now,
                    EventCount = run.EventCount,
                    Usage = run.Usage,
                    Cost = run.Cost,
                    NonTokenCost = run.NonTokenCost,
                    Error = unanswered is null ? null : Classify(ExpiredError(unanswered)),
                    TenantId = tenantId,
                },
                cancellationToken).ConfigureAwait(false);

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
