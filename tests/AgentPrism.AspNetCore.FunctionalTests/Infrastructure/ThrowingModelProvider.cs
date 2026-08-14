using Microsoft.Extensions.AI;

namespace AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Gercek saglayici SDK istisnalarini (orn. Anthropic'in <c>AnthropicApiException</c>'i,
/// OpenAI'nin <c>ClientResultException</c>'i) taklit eder: <see cref="Exception"/>'dan
/// DOGRUDAN turer, <see cref="AgentPrismException"/>/<see cref="InvalidOperationException"/>/
/// <see cref="HttpRequestException"/> DEGILDIR. Aile H (HATA-S2-003/HATA-S3-005) regresyon
/// testleri icindir: akissiz uclarin dar 'when' filtresi tam bu sinifi kacirirdi.
/// </summary>
internal sealed class FakeUpstreamOutageException(string message) : Exception(message);

/// <summary>Her cagrida <see cref="FakeUpstreamOutageException"/> firlatan model saglayicisi.</summary>
internal sealed class ThrowingModelProvider(string name = "kirik") : IModelProvider
{
    public string Name { get; } = name;

    public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = "kirik-1" }];

    public IChatClient CreateChatClient(ModelBinding binding) => new ThrowingChatClient();

    private sealed class ThrowingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new FakeUpstreamOutageException("Saglayici DNS cozemedi.");

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new FakeUpstreamOutageException("Saglayici DNS cozemedi.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // Sahte istemcinin serbest birakilacak kaynagi yok.
        }
    }
}
