using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Background service that periodically closes <c>Running</c> runs that have
/// not sent a heartbeat for a long time, marking them <c>Failed</c>.
/// </summary>
/// <remarks>
/// <para>
/// If a process crashes in the middle of <c>agent.RunAsync</c> (on the default
/// <c>MaxAttempts = 1</c> path), the row stays <c>Running</c> forever and
/// silently dilutes the denominator of <c>RunStatistics.ErrorRate</c> (
/// <c>settled = CompletedRuns + FailedRuns + CanceledRuns</c>). This service
/// finds that row, closes it, and records the reason.
/// </para>
/// <para>
/// While <see cref="RunReconciliationOptions.Enabled"/> is <see langword="false"/>
/// (the default), no SQL query is issued at all. Even when enabled, it
/// runs only ONE scan across the cluster -- <see cref="SingletonGuard"/> is
/// the SAME pattern <c>McpDiscoveryService</c> uses.
/// </para>
/// </remarks>
internal sealed class RunReconciliationService(
    IRunStore runStore,
    ISingletonLeaseStore leaseStore,
    IOptionsMonitor<RunReconciliationOptions> optionsMonitor,
    IOptionsMonitor<SingletonExecutionOptions> singletonOptionsMonitor,
    SchemaReadyGate schemaReadyGate,
    TimeProvider? timeProvider = null,
    ILogger<RunReconciliationService>? logger = null) : BackgroundService
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!optionsMonitor.CurrentValue.Enabled)
        {
            return;
        }

        // 🚨 Wait for the schema to be ready BEFORE the first SQL attempt (K-354).
        try
        {
            await schemaReadyGate.WaitAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Single-executor selection (Phase 42): when disabled (the default),
        // guard.IsHeld is always true and RunAsync returns immediately without
        // issuing any query to the store.
        var guard = new SingletonGuard(leaseStore, singletonOptionsMonitor, "run-reconciliation", logger);
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

    private async Task TickAsync(CancellationToken stoppingToken)
    {
        var options = optionsMonitor.CurrentValue;
        var staleBefore = _clock.GetUtcNow() - options.OrphanThreshold;

        try
        {
            var claimed = await runStore
                .ClaimOrphanedRunsAsync(staleBefore, options.MaxRunsPerScan, stoppingToken)
                .ConfigureAwait(false);

            if (claimed.Count > 0 && logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    "{Count} orphaned run(s) closed (threshold: {StaleBefore:O}).",
                    claimed.Count,
                    staleBefore);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A reconciliation pass must never crash the process: a failure
            // is retried on the next tick.
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "Orphaned run reconciliation pass failed.");
            }
        }
    }
}
