using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.AI;

namespace Tracon.Benchmarks;

/// <summary>
/// Measures the allocation the shared telemetry ring adds to one model call that nobody listens to - the cost every
/// call of every agent pays for the span and the metrics it may write.
/// </summary>
/// <remarks>
/// The client under the ring answers from a reply built once, so what is measured is the ring: the options of the
/// call and the hop to the lane that owns the telemetry client. With no listener on the source the lane writes no span.
/// </remarks>
[MemoryDiagnoser]
public class ChatTelemetryRingBenchmarks : IDisposable
{
    private ChatTelemetryLanes _lanes = null!;
    private IChatClient _ring = null!;
    private ChatMessage[] _messages = null!;
    private ChatOptions _options = null!;

    [GlobalSetup]
    public void Setup()
    {
        _lanes = new ChatTelemetryLanes("Tracon.Benchmarks.ChatTelemetryRing");
        _ring = _lanes.Wrap(new ReplyingChatClient());
        _messages = [new ChatMessage(ChatRole.User, "Hello.")];
        _options = new ChatOptions { ModelId = "bench-model" };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _lanes.Dispose();
        GC.SuppressFinalize(this);
    }

    [Benchmark]
    public Task<ChatResponse> ModelCall() => _ring.GetResponseAsync(_messages, _options);

    /// <summary>A chat client that gives the same reply to every call and names itself like a provider client does.</summary>
    private sealed class ReplyingChatClient : IChatClient
    {
        private static readonly Task<ChatResponse> Reply = Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hi.")));

        private static readonly ChatClientMetadata Metadata = new("bench-provider", new Uri("https://bench.example/v1"), "bench-model");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Reply;

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Benchmark client - only the reply path is measured.");

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType == typeof(ChatClientMetadata) ? Metadata : null;

        public void Dispose()
        {
        }
    }
}
