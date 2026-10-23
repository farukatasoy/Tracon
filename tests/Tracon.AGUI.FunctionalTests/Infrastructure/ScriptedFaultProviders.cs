using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Tracon.AGUI.FunctionalTests.Infrastructure;

/// <summary>A provider SDK exception: it derives directly from <see cref="Exception"/>, like a real provider's does.</summary>
internal sealed class FakeUpstreamOutageException(string message) : Exception(message);

/// <summary>A model provider whose stream fails after it began, with a message that names an internal address.</summary>
internal sealed class FailingModelProvider(string name, string message) : IModelProvider
{
    public string Name { get; } = name;

    public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = "failing-1" }];

    public IChatClient CreateChatClient(ModelBinding binding) => new FailingChatClient(message);

    private sealed class FailingChatClient(string message) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new FakeUpstreamOutageException(message);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "partial answer");

            await Task.Yield();

            throw new FakeUpstreamOutageException(message);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // Nothing to release.
        }
    }
}

/// <summary>
/// A model provider whose FIRST call waits until the test lets it go; every later call answers at once.
/// </summary>
/// <remarks>
/// It is how a test makes two runs of one thread overlap deterministically: the first run is held inside its model call
/// while the second runs and saves the session, then the first is released and finds the session changed under it.
/// </remarks>
internal sealed class GatedModelProvider(string name) : IModelProvider
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _calls;

    public string Name { get; } = name;

    public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = "gated-1" }];

    /// <summary>Completes once the first call is held.</summary>
    public Task FirstCallHeld => _started.Task;

    /// <summary>Lets the first call answer.</summary>
    public void ReleaseFirstCall() => _release.TrySetResult();

    public IChatClient CreateChatClient(ModelBinding binding) => new GatedChatClient(this);

    private sealed class GatedChatClient(GatedModelProvider owner) : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            await HoldFirstCallAsync(cancellationToken).ConfigureAwait(false);

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "answered"));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await HoldFirstCallAsync(cancellationToken).ConfigureAwait(false);

            yield return new ChatResponseUpdate(ChatRole.Assistant, "answered");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // Nothing to release.
        }

        private async Task HoldFirstCallAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref owner._calls) != 1)
            {
                return;
            }

            owner._started.TrySetResult();

            await owner._release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
