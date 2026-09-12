using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Tracon.Embedded;

/// <summary>
/// Sample model provider that makes no network calls — the same pattern
/// <c>samples/Tracon.Api</c> uses, extended here with one scripted tool
/// call so a real run exercises <c>TraconRunContext.Current</c>: on its
/// first turn it calls <c>current_account</c> if the agent offers it, then
/// embeds the tool's result in its final answer.
/// </summary>
internal sealed class EchoModelProvider : IModelProvider
{
    private const string ProbeToolName = "current_account";

    public string Name => "echo";

    public IReadOnlyList<ModelDescriptor> Models { get; } =
    [
        new ModelDescriptor
        {
            Name = "echo-1",
            DisplayName = "Echo (local, no network)",
            ContextWindowTokens = 8_192,
            MaxOutputTokens = 1_024,
            SupportsTools = true,
        },
    ];

    public IChatClient CreateChatClient(ModelBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return new EchoChatClient(binding.Model);
    }

    private sealed class EchoChatClient(string modelId) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var content = BuildContent(messages, options);

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [content]))
            {
                ModelId = modelId,
                Usage = new UsageDetails { InputTokenCount = 8, OutputTokenCount = 8, TotalTokenCount = 16 },
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var content = BuildContent(messages, options);
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();

            yield return new ChatResponseUpdate(ChatRole.Assistant, [content]) { ModelId = modelId };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // The local client has no resources to release.
        }

        /// <summary>
        /// On the first turn, calls <see cref="ProbeToolName"/> if the agent
        /// offers it. On the turn that follows (the tool's result is now in
        /// history), the final answer embeds that result — proving the tool
        /// body actually ran, with <c>TraconRunContext.Current</c>
        /// populated, rather than the provider fabricating the text itself.
        /// </summary>
        private static AIContent BuildContent(IEnumerable<ChatMessage> messages, ChatOptions? options)
        {
            var messageList = messages as IReadOnlyList<ChatMessage> ?? [.. messages];

            var toolResult = messageList
                .SelectMany(static message => message.Contents)
                .OfType<FunctionResultContent>()
                .LastOrDefault();

            if (toolResult is not null)
            {
                return new TextContent($"Echo: {toolResult.Result}");
            }

            var probeTool = options?.Tools?.OfType<AIFunction>()
                .FirstOrDefault(static tool => string.Equals(tool.Name, ProbeToolName, StringComparison.Ordinal));

            if (probeTool is not null)
            {
                return new FunctionCallContent(Guid.NewGuid().ToString("N"), probeTool.Name);
            }

            var lastUserMessage = messageList.LastOrDefault(static message => message.Role == ChatRole.User);
            return new TextContent($"Echo: {lastUserMessage?.Text ?? "(empty request)"}");
        }
    }
}
