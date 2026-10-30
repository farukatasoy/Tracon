using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// A model provider whose turns are scripted lists of <see cref="AIContent"/>, one list per model call, in order;
/// the last list repeats. It is <see cref="ContentModelProvider"/> for a run that needs more than one turn: a
/// turn that holds a hosted tool call AND a function call makes the tool-call loop ask the model again.
/// </summary>
/// <remarks>
/// It states every hosted tool kind, keeps the options of every call and sends a streamed turn content by content.
/// </remarks>
internal sealed class TurnsModelProvider(string name, params IReadOnlyList<AIContent>[] turns) : IModelProvider, IHostedToolSupport
{
    private readonly List<ChatOptions?> _options = [];
    private int _calls;

    public string Name { get; } = name;

    public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = $"{name}-1" }];

    public HostedToolKinds SupportedHostedTools => ContentModelProvider.AllHostedTools;

    /// <summary>The options of every model call, oldest first.</summary>
    public IReadOnlyList<ChatOptions?> Options
    {
        get
        {
            lock (_options)
            {
                return [.. _options];
            }
        }
    }

    public IChatClient CreateChatClient(ModelBinding binding) => new Client(this);

    private IReadOnlyList<AIContent> Next(ChatOptions? options)
    {
        lock (_options)
        {
            _options.Add(options);
            var index = Math.Min(_calls++, turns.Length - 1);

            return turns[index];
        }
    }

    private sealed class Client(TurnsModelProvider owner) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [.. owner.Next(options)])));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var content in owner.Next(options))
            {
                await Task.Yield();
                yield return new ChatResponseUpdate(ChatRole.Assistant, [content]);
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // Nothing to release.
        }
    }
}
