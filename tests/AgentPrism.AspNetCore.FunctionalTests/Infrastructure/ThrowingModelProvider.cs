using Microsoft.Extensions.AI;

namespace AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Fakes a real provider SDK exception (e.g. Anthropic's <c>AnthropicApiException</c>,
/// OpenAI's <c>ClientResultException</c>): it derives DIRECTLY from <see cref="Exception"/>,
/// NOT from <see cref="AgentPrismException"/>/<see cref="InvalidOperationException"/>/
/// <see cref="HttpRequestException"/>. This is for the family H (HATA-S2-003/HATA-S3-005)
/// regression tests: the non-streaming endpoints' narrow 'when' filter would have missed
/// exactly this class.
/// </summary>
internal sealed class FakeUpstreamOutageException(string message) : Exception(message);

/// <summary>A model provider that throws <see cref="FakeUpstreamOutageException"/> on every call.</summary>
internal sealed class ThrowingModelProvider(string name = "kirik", string message = "The provider failed to resolve DNS.") : IModelProvider
{
    public string Name { get; } = name;

    public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = "kirik-1" }];

    public IChatClient CreateChatClient(ModelBinding binding) => new ThrowingChatClient(message);

    private sealed class ThrowingChatClient(string message) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new FakeUpstreamOutageException(message);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new FakeUpstreamOutageException(message);

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // The fake client has no resources to release.
        }
    }
}
