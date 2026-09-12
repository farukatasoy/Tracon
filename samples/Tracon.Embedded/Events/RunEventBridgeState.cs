namespace Tracon.Embedded;

/// <summary>
/// Counters the bridge updates, read by the sample's own diagnostics and by
/// <c>EmbeddedSampleTests</c> — a real host would publish these as metrics
/// instead.
/// </summary>
internal sealed class RunEventBridgeState
{
    private int _received;
    private int _dropped;

    /// <summary>The number of events the background consumer has drained so far.</summary>
    public int Received => Volatile.Read(ref _received);

    /// <summary>
    /// The number of events dropped because the channel was full when
    /// <see cref="BoundedChannelRunEventSink.OnEventAsync"/> tried to queue them.
    /// </summary>
    public int Dropped => Volatile.Read(ref _dropped);

    public void RecordReceived() => Interlocked.Increment(ref _received);

    public void RecordDropped() => Interlocked.Increment(ref _dropped);
}
