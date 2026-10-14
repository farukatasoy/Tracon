using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// A background service that periodically closes expired pending approval requests with
/// <see cref="ApprovalStatus.Expired"/> status and changes their runs to <see cref="RunStatus.Failed"/>.
/// </summary>
/// <remarks>
/// <para>
/// Uses the same pattern as <see cref="RunReconciliationService"/>. When
/// <see cref="TraconApprovalOptions.ExpirationEnabled"/> is enabled, only one instance
/// scans across the cluster. It uses <see cref="SingletonGuard"/> for that.
/// </para>
/// <para>
/// For every closed request, this service also changes its <c>runs</c> row to
/// <see cref="RunStatus.Failed"/>. Otherwise, a client could see
/// <see cref="RunStatus.AwaitingApproval"/> forever through <c>GET /api/runs/{id}</c>.
/// <c>IRunStore.CompleteRunAsync</c> does not check the previous status and replaces the
/// usage, cost and event count the row holds, so the service reads the run first, closes it only
/// while it is still <see cref="RunStatus.AwaitingApproval"/>, and writes those values back.
/// </para>
/// </remarks>
internal sealed class ApprovalExpirationService(
    IPendingApprovalStore approvalStore,
    IRunStore runStore,
    ISingletonLeaseStore leaseStore,
    IOptionsMonitor<TraconApprovalOptions> optionsMonitor,
    IOptionsMonitor<SingletonExecutionOptions> singletonOptionsMonitor,
    SchemaReadyGate schemaReadyGate,
    TimeProvider? timeProvider = null,
    ILogger<ApprovalExpirationService>? logger = null) : BackgroundService
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!optionsMonitor.CurrentValue.ExpirationEnabled)
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

        var guard = new SingletonGuard(leaseStore, singletonOptionsMonitor, "approval-expiration", logger);
        var guardTask = guard.RunAsync(stoppingToken);

        using var timer = new PeriodicTimer(optionsMonitor.CurrentValue.ScanInterval);

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

    internal async Task TickAsync(CancellationToken stoppingToken)
    {
        var options = optionsMonitor.CurrentValue;
        var now = _clock.GetUtcNow();

        try
        {
            var expired = await approvalStore.ExpireAsync(now, options.MaxPerScan, stoppingToken).ConfigureAwait(false);

            foreach (var approval in expired)
            {
                await FailRunAsync(approval, now, stoppingToken).ConfigureAwait(false);
            }

            if (expired.Count > 0 && logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning("{Count} approval requests expired and were closed.", expired.Count);
            }
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, stoppingToken))
        {
            // A scan must not terminate the service. The next cycle retries after an error.
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "The approval expiration scan failed.");
            }
        }
    }

    private async ValueTask FailRunAsync(PendingApproval approval, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            // The run is the approval's tenant's, and this service runs in no request.
            using var tenantScope = AmbientTenantScope.Begin(approval.TenantId);

            var run = await runStore.GetRunAsync(approval.RunId, cancellationToken).ConfigureAwait(false);

            // Only a run that still waits is closed. A run something else already closed keeps the
            // outcome it has: writing over it would replace what that recorded.
            if (run is null || run.Status != RunStatus.AwaitingApproval)
            {
                return;
            }

            // 🚨 CompleteRunAsync REPLACES the event count, the usage and the cost with what the
            // completion carries. Built from nothing, the completion erased what the run spent
            // before it paused for this approval; the values go back as the row holds them.
            await runStore.CompleteRunAsync(
                new RunCompletion
                {
                    RunId = approval.RunId,
                    Status = RunStatus.Failed,
                    CompletedAt = now,
                    EventCount = run.EventCount,
                    Usage = run.Usage,
                    Cost = run.Cost,
                    NonTokenCost = run.NonTokenCost,
                    Error = new RunError
                    {
                        Type = "ApprovalExpired",
                        Message = $"The approval request for tool '{approval.ToolName}' expired at " +
                                  $"{approval.ExpiresAt:O}.",
                    },
                    TenantId = approval.TenantId,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    exception,
                    "Could not change the run for an expired approval request to Failed: {RunId}.",
                    approval.RunId);
            }
        }
    }
}
