using System.Runtime.CompilerServices;
using AgentPrism;
using Microsoft.Extensions.AI;

namespace AgentPrism.Api;

/// <summary>
/// Ag cagrisi yapmayan ornek model saglayicisi.
/// </summary>
/// <remarks>
/// <para>
/// Faz 1'de gercek bir saglayici henuz yok. Bu sinif iki isi birden yapar:
/// ornegin API anahtari olmadan calismasini saglar ve <see cref="IModelProvider"/>
/// genisleme noktasinin gercekten calistigini gosterir.
/// </para>
/// <para>
/// Faz 3'te <c>builder.AddAgentPrism().UseOpenAI(apiKey)</c> bu saglayicinin
/// yerini alir; agent tanimlarinda yalnizca <see cref="ModelBinding.Provider"/>
/// degeri degisir, baska hicbir sey degismez.
/// </para>
/// </remarks>
internal sealed class EchoModelProvider : IModelProvider
{
    public string Name => "echo";

    public IReadOnlyList<ModelDescriptor> Models { get; } =
    [
        new ModelDescriptor
        {
            Name = "echo-1",
            DisplayName = "Echo (yerel, ag yok)",
            ContextWindowTokens = 8_192,
            MaxOutputTokens = 1_024,
            SupportsTools = false,
        },
    ];

    public IChatClient CreateChatClient(ModelBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return new EchoChatClient(binding.Model);
    }

    private sealed class EchoChatClient : IChatClient
    {
        private readonly string _modelId;

        public EchoChatClient(string modelId) => _modelId = modelId;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var reply = BuildReply(messages);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply))
            {
                ModelId = _modelId,
                Usage = new UsageDetails
                {
                    InputTokenCount = reply.Length / 4,
                    OutputTokenCount = reply.Length / 4,
                    TotalTokenCount = reply.Length / 2,
                },
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var word in BuildReply(messages).Split(' '))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new ChatResponseUpdate(ChatRole.Assistant, word + ' ') { ModelId = _modelId };
                await Task.Delay(30, cancellationToken).ConfigureAwait(false);
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // Yerel istemcinin serbest birakilacak kaynagi yok.
        }

        private static string BuildReply(IEnumerable<ChatMessage> messages)
        {
            var lastUserMessage = messages.LastOrDefault(static message => message.Role == ChatRole.User);
            return $"Echo: {lastUserMessage?.Text ?? "(bos istek)"}";
        }
    }
}
