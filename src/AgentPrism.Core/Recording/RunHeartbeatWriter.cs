using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// A background service that periodically writes a "still here" marker in bulk
/// for runs that are active in this process (Phase 54).
/// </summary>
/// <remarks>
/// <para>
/// It sends one query per cycle, not per run. It takes the current snapshot of
/// <see cref="IRunCancellationRegistry.ActiveRunIds"/> and calls
/// <see cref="IRunStore.TouchHeartbeatAsync"/> once. For N active runs, this is a
/// single bulk marker that adds nothing to the hot path instead of N writes. See
/// <c>docs/54-OKSUZ-CALISTIRMA-UZLASTIRMASI.md</c>, Open Question 1.
/// </para>
/// <para>
/// When <see cref="RunReconciliationOptions.Enabled"/> is <see langword="false"/>
/// by default, no marker is written. There is no value
/// in maintaining a marker when the reconciler is not running (K1).
/// </para>
/// </remarks>
internal sealed class RunHeartbeatWriter(
    IRunStore runStore,
    IRunCancellationRegistry cancellationRegistry,
    IOptionsMonitor<RunReconciliationOptions> optionsMonitor,
    SchemaReadyGate schemaReadyGate,
    TimeProvider? timeProvider = null,
    ILogger<RunHeartbeatWriter>? logger = null) : BackgroundService
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = optionsMonitor.CurrentValue;

        if (!options.Enabled)
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

        using var timer = new PeriodicTimer(optionsMonitor.CurrentValue.HeartbeatInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task TickAsync(CancellationToken stoppingToken)
    {
        var activeRunIds = cancellationRegistry.ActiveRunIds;

        if (activeRunIds.Count == 0)
        {
            return;
        }

        try
        {
            await runStore.TouchHeartbeatAsync(activeRunIds, _clock.GetUtcNow(), stoppingToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Observability does not break functionality. A failed cycle does not
            // affect active runs and the next cycle retries.
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "The run heartbeat cycle failed.");
            }
        }
    }
}
