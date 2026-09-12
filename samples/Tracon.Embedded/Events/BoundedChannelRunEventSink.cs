using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Tracon.Embedded;

/// <summary>
/// 🚨 <strong>DEMONSTRATION ONLY.</strong> Bridges run events into a bounded
/// in-process channel that a background worker drains — the same shape a real
/// bridge to Kafka, SQS, or an internal event bus takes.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OnEventAsync"/> is on the run's own hot path (see
/// <see cref="IRunEventSink"/>'s remarks): it must queue the event and return,
/// never perform I/O inline. <see cref="ChannelWriter{T}.TryWrite"/> is
/// synchronous and never blocks; when the channel is at capacity it returns
/// <see langword="false"/> immediately instead of waiting for room, which is
/// what makes this a DROPPING bridge rather than a blocking one.
/// </para>
/// <para>
/// A small capacity is chosen deliberately so the sample's own manual test
/// case can overflow it with a normal burst of events instead of needing a
/// slow consumer to prove the drop path.
/// </para>
/// <para>
/// The channel type itself is not a Tracon type and is not offered as
/// one — this class exists only in the sample. Tracon takes no dependency
/// on any specific messaging library; you point this pattern at whichever bus
/// your host already runs.
/// </para>
/// </remarks>
internal sealed class BoundedChannelRunEventSink : IRunEventSink
{
    private readonly Channel<RunEvent> _channel;
    private readonly RunEventBridgeState _state;
    private readonly ILogger<BoundedChannelRunEventSink> _logger;

    public BoundedChannelRunEventSink(RunEventBridgeState state, ILogger<BoundedChannelRunEventSink> logger)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(logger);

        _state = state;
        _logger = logger;
        _channel = Channel.CreateBounded<RunEvent>(new BoundedChannelOptions(capacity: 8)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>The reader side, consumed by <see cref="RunEventBridgeWorker"/>.</summary>
    public ChannelReader<RunEvent> Reader => _channel.Reader;

    /// <inheritdoc />
    public ValueTask OnEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
    {
        if (!_channel.Writer.TryWrite(runEvent))
        {
            _state.RecordDropped();
            _logger.LogWarning(
                "Run event bridge is full; dropped a {EventType} event for run {RunId}. Total dropped: {Dropped}.",
                runEvent.Type,
                runEvent.RunId,
                _state.Dropped);
        }

        return default;
    }
}
