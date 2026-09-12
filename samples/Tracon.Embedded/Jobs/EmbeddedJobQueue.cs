using System.Threading.Channels;

namespace Tracon.Embedded;

/// <summary>
/// The host's own background work queue — not a Tracon type. It stands
/// in for whatever the host already uses (Hangfire, a cloud queue, a plain
/// hosted service) to run work that has no HTTP request behind it.
/// </summary>
internal sealed class EmbeddedJobQueue
{
    private readonly Channel<EmbeddedJob> _channel = Channel.CreateUnbounded<EmbeddedJob>();

    public ChannelReader<EmbeddedJob> Reader => _channel.Reader;

    public void Enqueue(EmbeddedJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        _channel.Writer.TryWrite(job);
    }
}
