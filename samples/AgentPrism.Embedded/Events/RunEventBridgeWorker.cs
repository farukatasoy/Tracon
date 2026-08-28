using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentPrism.Embedded;

/// <summary>
/// 🚨 <strong>DEMONSTRATION ONLY.</strong> Drains <see cref="BoundedChannelRunEventSink"/>
/// the way a real consumer would drain a queue subscription — outside the run's
/// own hot path entirely.
/// </summary>
internal sealed class RunEventBridgeWorker(
    BoundedChannelRunEventSink sink,
    RunEventBridgeState state,
    ILogger<RunEventBridgeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var runEvent in sink.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            state.RecordReceived();
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(
                    "Bridged {EventType} event for run {RunId} (seq {Sequence}).",
                    runEvent.Type,
                    runEvent.RunId,
                    runEvent.Sequence);
            }

            // Stands in for a real publish call to an external bus (network I/O
            // has latency the local echo model does not). Without this delay the
            // drain loop keeps up with even a large burst on this machine, and
            // the sink's drop path — the whole point of a BOUNDED channel — would
            // never actually fire outside a contrived, hard-to-reproduce race.
            await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken).ConfigureAwait(false);
        }
    }
}
