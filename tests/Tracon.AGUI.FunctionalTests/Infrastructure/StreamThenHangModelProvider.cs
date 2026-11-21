using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Tracon.AGUI.FunctionalTests.Infrastructure;

/// <summary>
/// A model provider that streams a first piece of text and then waits until the caller goes away, so a test cancels a
/// turn that the model has already started to answer.
/// </summary>
internal sealed class StreamThenHangModelProvider(string name, string firstChunk) : IModelProvider
{
    private readonly TaskCompletionSource _chunkSent = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Name { get; } = name;

    public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = "stream-hang-1" }];

    /// <summary>Completes once the first piece of text has been handed to the caller.</summary>
    public Task ChunkSent => _chunkSent.Task;

    public IChatClient CreateChatClient(ModelBinding binding) => new Client(_chunkSent, firstChunk);

    private sealed class Client(TaskCompletionSource chunkSent, string firstChunk) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This provider only streams.");

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, firstChunk);

            chunkSent.TrySetResult();

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); // delay: simulated

            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
