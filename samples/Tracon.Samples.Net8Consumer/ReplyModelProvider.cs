using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Tracon.Samples.Net8Consumer;

/// <summary>A model provider that answers locally with the last user message.</summary>
internal sealed class ReplyModelProvider : IModelProvider
{
    public const string ProviderName = "reply";

    public const string ModelName = "reply-1";

    public string Name => ProviderName;

    public IReadOnlyList<ModelDescriptor> Models { get; } =
    [
        new ModelDescriptor { Name = ModelName, DisplayName = "Reply (local, no network)" },
    ];

    public IChatClient CreateChatClient(ModelBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return new ReplyChatClient(binding.Model);
    }

    private sealed class ReplyChatClient(string modelId) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Reply(messages)))
            {
                ModelId = modelId,
                FinishReason = ChatFinishReason.Stop,
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, Reply(messages)) { ModelId = modelId };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }

        private static string Reply(IEnumerable<ChatMessage> messages)
        {
            var lastUserText = messages
                .Where(static message => message.Role == ChatRole.User)
                .Select(static message => message.Text)
                .LastOrDefault(static text => !string.IsNullOrWhiteSpace(text));

            return $"reply: {lastUserText}";
        }
    }
}
