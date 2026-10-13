using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Tracon;

namespace Tracon.Api;

/// <summary>
/// Sample model provider that answers after a fixed delay and makes no network call.
/// </summary>
/// <remarks>
/// Registered only when <c>Tracon:Demo:SlowModel:Seconds</c> is set. It stands in for a
/// writer step that takes minutes, so a workflow step's own timeout
/// (<see cref="WorkflowNodeReference.Timeout"/>) can be seen to stop it: the delay
/// honours the token the step is given, so a timeout ends the call at once.
/// </remarks>
/// <param name="delay">How long every call waits before it answers.</param>
internal sealed class SlowModelProvider(TimeSpan delay) : IModelProvider
{
    public string Name => "slow";

    public IReadOnlyList<ModelDescriptor> Models { get; } =
    [
        new ModelDescriptor
        {
            Name = "slow-1",
            DisplayName = "Slow echo (local, no network)",
            ContextWindowTokens = 8_192,
            MaxOutputTokens = 1_024,
            SupportsTools = false,
        },
    ];

    public IChatClient CreateChatClient(ModelBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        return new SlowChatClient(delay);
    }

    private sealed class SlowChatClient(TimeSpan delay) : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, Answer(messages)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

            yield return new ChatResponseUpdate(ChatRole.Assistant, Answer(messages));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // Nothing to release.
        }

        private string Answer(IEnumerable<ChatMessage> messages)
            => $"[slow after {delay.TotalSeconds:0.#} s] {messages.LastOrDefault(static message => message.Role == ChatRole.User)?.Text}";
    }
}
