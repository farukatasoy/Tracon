using Microsoft.Extensions.AI;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// A model provider that reports its OWN request timeout the way
/// <see cref="HttpClient"/> does — an <see cref="OperationCanceledException"/>
/// raised while NOTHING was cancelled (K-737).
/// </summary>
/// <remarks>
/// This is the counterpart of <see cref="HangingModelProvider"/>: that one
/// makes a wait limit fire, this one makes sure no limit fires at all, so a
/// caller that reads the two as the same event is caught.
/// </remarks>
internal sealed class TimingOutModelProvider(string name) : IModelProvider
{
    public string Name { get; } = name;

    public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = "timing-out-1" }];

    public IChatClient CreateChatClient(ModelBinding binding) => new TimingOutChatClient();

    private sealed class TimingOutChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw Timeout();

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw Timeout();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // The fake client has no resource to release.
        }

        private static TaskCanceledException Timeout()
        {
            const string Message = "The request to https://api.example/v1 timed out after 30s.";

            return new TaskCanceledException(Message, new TimeoutException(Message));
        }
    }
}
