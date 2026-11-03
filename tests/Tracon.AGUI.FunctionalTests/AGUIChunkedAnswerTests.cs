using System.Runtime.CompilerServices;
using AGUI.Abstractions;
using Microsoft.Extensions.AI;
using Tracon.AGUI.FunctionalTests.Infrastructure;

namespace Tracon.AGUI.FunctionalTests;

/// <summary>
/// An answer the model streams in several chunks is one assistant message on the AG-UI stream, whether or not the provider
/// put a message id on the chunks.
/// </summary>
/// <remarks>
/// <para>
/// Found with the official AG-UI JavaScript client against the example host: its echo model streams one chunk per word,
/// and the client assembled seven assistant messages for one answer. A front end draws a message per
/// <c>TEXT_MESSAGE_START</c>, so the user saw seven bubbles. The protocol's own rule is one start, any number of content
/// events with the same message id, one end. Microsoft.Extensions.AI treats a chunk without an id as a continuation of
/// the message before it, and the converter that writes the AG-UI events has to see the stream that way.
/// </para>
/// </remarks>
public sealed class AGUIChunkedAnswerTests
{
    private static readonly string[] Chunks = ["alpha ", "beta ", "gamma"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_chunked_answer_is_one_text_message_with_one_id_whatever_the_provider_sets_on_the_chunks(bool providerSetsMessageId)
    {
        await using var host = await AGUITestHost.StartAsync(builder => builder
            .AddModelProvider(new ChunkedModelProvider("chunked-model", Chunks, providerSetsMessageId))
            .AddAgent(TestAgents.Definition("chunked-agent", "chunked-model")));

        var response = await host.RunAsync("chunked-agent", AGUIInputs.User("t-chunked", "go"));

        response.Text.ShouldBe("alpha beta gamma");

        var starts = response.Events.OfType<TextMessageStartEvent>().ToList();
        var ends = response.Events.OfType<TextMessageEndEvent>().ToList();
        var ids = response.Events.OfType<TextMessageContentEvent>().Select(static e => e.MessageId).Distinct(StringComparer.Ordinal).ToList();

        starts.Count.ShouldBe(1, $"one answer is one message; the stream was: {string.Join(",", response.Types)}");
        ends.Count.ShouldBe(1);
        ids.Count.ShouldBe(1);
        starts[0].MessageId.ShouldBe(ids[0]);
        ends[0].MessageId.ShouldBe(ids[0]);
    }

    /// <summary>A provider that streams the given chunks, with or without the same message id on each of them.</summary>
    private sealed class ChunkedModelProvider(string name, string[] chunks, bool setMessageId) : IModelProvider
    {
        public string Name { get; } = name;

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = name + "-1" }];

        public IChatClient CreateChatClient(ModelBinding binding) => new ChunkedChatClient(chunks, setMessageId);

        private sealed class ChunkedChatClient(string[] chunks, bool setMessageId) : IChatClient
        {
            public Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
                => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Concat(chunks))));

            public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                foreach (var chunk in chunks)
                {
                    await Task.Yield();

                    yield return new ChatResponseUpdate(ChatRole.Assistant, chunk) { MessageId = setMessageId ? "provider-message-1" : null };
                }
            }

            public object? GetService(Type serviceType, object? serviceKey = null) => null;

            public void Dispose()
            {
                // Nothing to release.
            }
        }
    }
}
