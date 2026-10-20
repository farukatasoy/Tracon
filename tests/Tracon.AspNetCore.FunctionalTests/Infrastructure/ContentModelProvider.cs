using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// A model provider whose every turn returns the same scripted <see cref="AIContent"/> list: the one
/// thing <c>Tracon.Testing.FakeModelProvider</c> (text and tool calls only) cannot script. Used to put
/// hosted tool, error and data content through the real run recording pipeline (F-589).
/// </summary>
/// <remarks>
/// The buffered path returns one message holding every content; the streaming path sends each content
/// as its own update, the way a provider adapter does.
/// </remarks>
internal sealed class ContentModelProvider(IReadOnlyList<AIContent> contents, string name = "scripted") : IModelProvider
{
    public string Name { get; } = name;

    public IReadOnlyList<ModelDescriptor> Models { get; } =
        [new ModelDescriptor { Name = $"{name}-1" }];

    public IChatClient CreateChatClient(ModelBinding binding) => new Client(contents);

    private sealed class Client(IReadOnlyList<AIContent> contents) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [.. contents])));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var content in contents)
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
