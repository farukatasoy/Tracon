using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Tracon.AGUI.FunctionalTests.Infrastructure;

/// <summary>
/// A model provider whose chat client never answers on its own: it stops only when the token it was handed is cancelled.
/// </summary>
/// <remarks>
/// <see cref="Started"/> completes once the model was called, so a test cancels the request at a moment it knows the run is
/// in flight, not after a delay.
/// </remarks>
internal sealed class HangingModelProvider(string name) : IModelProvider
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Name { get; } = name;

    public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = "hanging-1" }];

    public Task Started => _started.Task;

    public IChatClient CreateChatClient(ModelBinding binding) => new HangingChatClient(_started);

    private sealed class HangingChatClient(TaskCompletionSource started) : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            started.TrySetResult();

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); // delay: simulated

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "unreachable"));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            started.TrySetResult();

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); // delay: simulated

            yield return new ChatResponseUpdate(ChatRole.Assistant, "unreachable");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // Nothing to release.
        }
    }
}
