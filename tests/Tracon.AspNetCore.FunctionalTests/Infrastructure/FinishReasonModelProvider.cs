using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// A model provider whose turns carry a scripted <see cref="ChatFinishReason"/>
/// and usage — the one thing <c>Tracon.Testing.FakeModelProvider</c> cannot
/// script. Used to put a response stopped at the output limit
/// (<see cref="ChatFinishReason.Length"/>) through the real pipeline (F-320).
/// </summary>
/// <remarks>
/// The streaming path splits a text turn into two updates, the second carrying
/// the finish reason and the usage — the shape the Anthropic adapter produces
/// (the reason arrives with the last frame).
/// </remarks>
internal sealed class FinishReasonModelProvider(string name = "scripted") : IModelProvider
{
    private readonly ConcurrentQueue<Turn> _turns = new();
    private readonly ConcurrentQueue<ChatOptions?> _options = new();

    public string Name { get; } = name;

    public IReadOnlyList<ModelDescriptor> Models { get; } =
        [new ModelDescriptor { Name = $"{name}-1", SupportsStructuredOutput = true }];

    /// <summary>Gets how many model calls reached the provider.</summary>
    public int Calls => _options.Count;

    /// <summary>Gets the options of every call, in order.</summary>
    public IReadOnlyList<ChatOptions?> Options => [.. _options];

    /// <summary>Queues a text turn.</summary>
    public FinishReasonModelProvider Text(string text, ChatFinishReason finishReason, int inputTokens, int outputTokens)
    {
        _turns.Enqueue(new Turn(text, ToolName: null, finishReason, inputTokens, outputTokens));
        return this;
    }

    /// <summary>Queues a tool-call turn.</summary>
    public FinishReasonModelProvider ToolCall(string toolName, ChatFinishReason finishReason, int inputTokens, int outputTokens)
    {
        _turns.Enqueue(new Turn(Text: null, toolName, finishReason, inputTokens, outputTokens));
        return this;
    }

    public IChatClient CreateChatClient(ModelBinding binding) => new Client(this);

    private Turn Next(ChatOptions? options)
    {
        _options.Enqueue(options);

        return _turns.TryDequeue(out var turn)
            ? turn
            : throw new InvalidOperationException("The test script has no turn left.");
    }

    private sealed record Turn(string? Text, string? ToolName, ChatFinishReason FinishReason, int InputTokens, int OutputTokens)
    {
        public UsageDetails Usage => new()
        {
            InputTokenCount = InputTokens,
            OutputTokenCount = OutputTokens,
            TotalTokenCount = InputTokens + OutputTokens,
        };

        public AIContent Content => ToolName is { } tool
            ? new FunctionCallContent(Guid.NewGuid().ToString("N"), tool)
            : new TextContent(Text);
    }

    private sealed class Client(FinishReasonModelProvider owner) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var turn = owner.Next(options);

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [turn.Content]))
            {
                FinishReason = turn.FinishReason,
                Usage = turn.Usage,
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var turn = owner.Next(options);

            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, [turn.Content]);

            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(turn.Usage)])
            {
                FinishReason = turn.FinishReason,
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // Nothing to release.
        }
    }
}
