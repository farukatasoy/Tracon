using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AgentPrism.PostgreSql.IntegrationTests.Infrastructure;

/// <summary>
/// Aga cikmayan sahte model saglayicisi. Gelen son kullanici mesajini yankilar.
/// </summary>
public sealed class EchoModelProvider : IModelProvider, IDisposable
{
    private readonly EchoChatClient _client = new();

    /// <inheritdoc />
    public string Name => "echo";

    /// <inheritdoc />
    public IReadOnlyList<ModelDescriptor> Models { get; } =
    [
        new ModelDescriptor { Name = "echo-1", ContextWindowTokens = 8_192, MaxOutputTokens = 1_024 },
    ];

    /// <summary>Istemciye ulasan son mesaj listesi.</summary>
    public IReadOnlyList<ChatMessage> LastRequest => _client.LastRequest;

    /// <inheritdoc />
    public IChatClient CreateChatClient(ModelBinding binding) => _client;

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();

    private sealed class EchoChatClient : IChatClient
    {
        public List<ChatMessage> LastRequest { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest.Clear();
            LastRequest.AddRange(messages);

            var last = LastRequest.LastOrDefault(static message => message.Role == ChatRole.User);

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, $"Echo: {last?.Text}")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);

            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // Sahte istemcinin serbest birakilacak kaynagi yok.
        }
    }
}
