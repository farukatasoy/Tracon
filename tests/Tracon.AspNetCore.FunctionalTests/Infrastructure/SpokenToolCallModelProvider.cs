using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// A model provider whose turns can hold words AND a tool call, the way a real model answers "Goodbye." and calls
/// <c>end_conversation</c> in one response: the one thing <c>Tracon.Testing.FakeModelProvider</c> cannot script (a step of
/// its own is either words or a call).
/// </summary>
/// <remarks>
/// <para>
/// The streaming path sends the words in two updates and the call in a last one, the order a provider streams them in. With
/// <c>callInItsOwnMessage</c> the call carries a message identity of its own, the shape of a provider that returns the words
/// and the call as separate items of one response.
/// </para>
/// <para>
/// A request past the script is answered with the last tool result in the history, when there is one, and with a plain
/// sentence otherwise.
/// </para>
/// </remarks>
internal sealed class SpokenToolCallModelProvider(string name = "voice-script") : IModelProvider
{
    private readonly ConcurrentQueue<Turn> _turns = new();
    private readonly ConcurrentQueue<IReadOnlyList<ChatMessage>> _histories = new();
    private int _requests;

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = "voice-1" }];

    /// <summary>Gets how many requests reached the model.</summary>
    public int Requests => Volatile.Read(ref _requests);

    /// <summary>Gets the messages each request carried, in order.</summary>
    public IReadOnlyList<IReadOnlyList<ChatMessage>> Histories => [.. _histories];

    /// <summary>Queues a turn that only says <paramref name="words"/>.</summary>
    /// <param name="words">What the model says.</param>
    /// <returns>The provider, for chaining.</returns>
    public SpokenToolCallModelProvider Says(string words)
    {
        _turns.Enqueue(new Turn(words, Tool: null, CallInItsOwnMessage: false));

        return this;
    }

    /// <summary>Queues a turn that says <paramref name="words"/> and then calls a tool, in one response.</summary>
    /// <param name="words">What the model says before the call; empty for a call with no words.</param>
    /// <param name="tool">The tool the model calls.</param>
    /// <param name="callInItsOwnMessage">Whether the call comes as a message of its own, after the words.</param>
    /// <returns>The provider, for chaining.</returns>
    public SpokenToolCallModelProvider SaysThenCalls(string words, string tool, bool callInItsOwnMessage = false)
    {
        _turns.Enqueue(new Turn(words, tool, callInItsOwnMessage));

        return this;
    }

    /// <inheritdoc />
    public IChatClient CreateChatClient(ModelBinding binding) => new Client(this);

    private Turn Next(IEnumerable<ChatMessage> messages)
    {
        _histories.Enqueue([.. messages]);
        Interlocked.Increment(ref _requests);

        if (_turns.TryDequeue(out var turn))
        {
            return turn;
        }

        var lastResult = messages
            .SelectMany(static message => message.Contents)
            .OfType<FunctionResultContent>()
            .LastOrDefault();

        return new Turn(lastResult?.Result?.ToString() ?? "Fine.", Tool: null, CallInItsOwnMessage: false);
    }

    private sealed record Turn(string Words, string? Tool, bool CallInItsOwnMessage)
    {
        public List<ChatResponseUpdate> Updates()
        {
            var updates = new List<ChatResponseUpdate>();

            if (Words.Length > 0 && Tool is null)
            {
                updates.Add(new ChatResponseUpdate(ChatRole.Assistant, Words) { MessageId = "words" });
            }
            else if (Words.Length > 0)
            {
                // The words before a call arrive in two pieces, as a model streams them.
                var cut = Words.Length / 2;

                updates.Add(new ChatResponseUpdate(ChatRole.Assistant, Words[..cut]) { MessageId = "words" });
                updates.Add(new ChatResponseUpdate(ChatRole.Assistant, Words[cut..]) { MessageId = "words" });
            }

            if (Tool is not null)
            {
                updates.Add(new ChatResponseUpdate(
                    ChatRole.Assistant,
                    [new FunctionCallContent(Guid.NewGuid().ToString("N"), Tool, new Dictionary<string, object?>(StringComparer.Ordinal))])
                {
                    MessageId = CallInItsOwnMessage ? "call" : "words",
                });
            }

            return updates;
        }
    }

    private sealed class Client(SpokenToolCallModelProvider owner) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(owner.Next(messages).Updates().ToChatResponse());

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var update in owner.Next(messages).Updates())
            {
                await Task.Yield();

                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // Nothing to release.
        }
    }
}
