using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// A background service that periodically writes a "still here" marker in bulk for runs
/// that are active in this process.
/// </summary>
/// <remarks>
/// <para>
/// It sends one query per cycle, not per run. It takes the current snapshot of <see
/// cref="IRunCancellationRegistry.ActiveRunIds"/> and calls <see
/// cref="IRunStore.TouchHeartbeatAsync"/> once. For N active runs, this is a single
/// bulk marker that adds nothing to the hot path instead of N writes.
/// </para>
/// <para>
/// When <see cref="RunReconciliationOptions.Enabled"/> is <see langword="false"/> by
/// default, no marker is written. There is no value in maintaining a marker when the
/// reconciler is not running.
/// </para>
/// <para>
/// The same cycle reads cancellation requests. A cancel that landed on another
/// instance is written onto the run's row; the store hands it back from
/// <see cref="IRunStore.TouchHeartbeatAsync"/>, and this service calls
/// <see cref="IRunCancellationRegistry.TryCancel"/> for each one. The run then ends within
/// one <see cref="RunReconciliationOptions.HeartbeatInterval"/> plus the time the run takes
/// to observe its token. The request comes back every cycle until the run ends, and
/// <c>TryCancel</c> is idempotent.
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

    // The runs whose cancellation this process already logged. Only the service's own loop
    // touches it, and an entry leaves when its run leaves the registry.
    private readonly HashSet<Guid> _loggedCancellations = [];

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = optionsMonitor.CurrentValue;

        if (!options.Enabled)
        {
            return;
        }

        // Wait for the schema to become ready before the first SQL attempt.
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

        _loggedCancellations.IntersectWith(activeRunIds);

        if (activeRunIds.Count == 0)
        {
            return;
        }

        try
        {
            var requests = await runStore
                .TouchHeartbeatAsync(activeRunIds, _clock.GetUtcNow(), stoppingToken)
                .ConfigureAwait(false);

            foreach (var request in requests)
            {
                CancelRequestedRun(request);
            }
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, stoppingToken))
        {
            // Observability does not break functionality. A failed cycle does not
            // affect active runs and the next cycle retries; a cancellation request
            // stays on the row, so the next cycle reads it again.
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "The run heartbeat cycle failed.");
            }
        }
    }

    private void CancelRequestedRun(RunCancellationRequest request)
    {
        // The tenant is the run's own, as the row stores it: never the ambient one.
        if (!cancellationRegistry.TryCancel(request.RunId, request.TenantId)
            || !_loggedCancellations.Add(request.RunId))
        {
            return;
        }

        if (logger is not null && logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Run {RunId} was asked to cancel at {RequestedAt:O} from another instance; canceling it here.",
                request.RunId,
                request.RequestedAt);
        }
    }
}
