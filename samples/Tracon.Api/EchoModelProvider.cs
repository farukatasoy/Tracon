using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Tracon;

namespace Tracon.Api;

/// <summary>
/// Sample model provider that makes no network calls.
/// </summary>
/// <remarks>
/// <para>
/// It lets the sample run without an API key and demonstrates that the
/// <see cref="IModelProvider"/> extension point works end to end.
/// </para>
/// <para>
/// Calling <c>builder.AddTracon().UseOpenAI(apiKey)</c> replaces this provider;
/// agent definitions only change the <see cref="ModelBinding.Provider"/> value,
/// nothing else.
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
            DisplayName = "Echo (local, no network)",
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
            // The local client has no resources to release.
        }

        private static string BuildReply(IEnumerable<ChatMessage> messages)
        {
            var lastUserMessage = messages.LastOrDefault(static message => message.Role == ChatRole.User);
            return $"Echo: {lastUserMessage?.Text ?? "(empty request)"}";
        }
    }
}
