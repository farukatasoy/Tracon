using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// A model provider whose every turn returns the same scripted <see cref="AIContent"/> list: the one
/// thing <c>Tracon.Testing.FakeModelProvider</c> (text and tool calls only) cannot script. Used to put
/// hosted tool, error and data content through the real run recording pipeline (F-589).
/// </summary>
/// <remarks>
/// <para>
/// The buffered path returns one message holding every content; the streaming path sends each content
/// as its own update, the way a provider adapter does.
/// </para>
/// <para>
/// F-603: it states which hosted tools its surface runs (every kind unless a test narrows it) and keeps the
/// options of every call, so a test sees the tools that reached the model.
/// </para>
/// </remarks>
internal sealed class ContentModelProvider(
    IReadOnlyList<AIContent> contents,
    string name = "scripted",
    HostedToolKinds supportedHostedTools = ContentModelProvider.AllHostedTools)
    : IModelProvider, IHostedToolSupport
{
    /// <summary>Every kind a hosted tool can have.</summary>
    public const HostedToolKinds AllHostedTools =
        HostedToolKinds.WebSearch | HostedToolKinds.CodeInterpreter | HostedToolKinds.FileSearch;

    private readonly List<ChatOptions?> _options = [];

    public string Name { get; } = name;

    public IReadOnlyList<ModelDescriptor> Models { get; } =
        [new ModelDescriptor { Name = $"{name}-1" }];

    public HostedToolKinds SupportedHostedTools { get; } = supportedHostedTools;

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

    public IChatClient CreateChatClient(ModelBinding binding) => new Client(contents, Record);

    private void Record(ChatOptions? options)
    {
        lock (_options)
        {
            _options.Add(options);
        }
    }

    private sealed class Client(IReadOnlyList<AIContent> contents, Action<ChatOptions?> record) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            record(options);

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [.. contents])));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            record(options);

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
