using Microsoft.Extensions.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// The response cache keys a hosted tool by what it puts on the wire, not by its name.
/// </summary>
/// <remarks>
/// Every file search tool is named <c>file_search</c>, whatever it searches. Keyed by name, two agents of one tenant with the
/// same instructions, model and message but different vector stores would share an answer, and a tenant that replaced its
/// vector store would keep reading the old store's answers until the entry expired.
/// </remarks>
public sealed class HostedToolResponseCacheKeyTests
{
    private static readonly ChatMessage[] Messages = [new ChatMessage(ChatRole.User, "what does the manual say")];

    private static readonly ResponseCacheSettings Settings = new() { Enabled = true, Lifetime = TimeSpan.FromMinutes(10) };

    private static ChatOptions WithStore(string store)
        => new() { ModelId = "m", Tools = [new HostedFileSearchTool { Inputs = [new HostedVectorStoreContent(store)] }] };

    [Fact]
    public async Task Two_vector_stores_do_not_share_an_answer_and_one_store_does()
    {
        var inner = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        using var client = new TraconResponseCachingChatClient(inner, new FakeDistributedCache(), "openai-responses", "tenant-a", Settings);

        await client.GetResponseAsync(Messages, WithStore("vs_1"), TestContext.Current.CancellationToken);
        await client.GetResponseAsync(Messages, WithStore("vs_2"), TestContext.Current.CancellationToken);

        inner.CallCount.ShouldBe(2, "another vector store is another question");

        await client.GetResponseAsync(Messages, WithStore("vs_1"), TestContext.Current.CancellationToken);

        inner.CallCount.ShouldBe(2, "the same vector store is a hit");
    }

    [Fact]
    public async Task A_tool_the_description_cannot_compare_never_hits()
    {
        var inner = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        using var client = new TraconResponseCachingChatClient(inner, new FakeDistributedCache(), "openai-responses", "tenant-a", Settings);
        var options = new ChatOptions
        {
            ModelId = "m",
            Tools = [new HostedWebSearchTool(new Dictionary<string, object?>(StringComparer.Ordinal) { ["Filters"] = new object() })],
        };

        await client.GetResponseAsync(Messages, options, TestContext.Current.CancellationToken);
        await client.GetResponseAsync(Messages, options, TestContext.Current.CancellationToken);

        inner.CallCount.ShouldBe(2, "two filters that cannot be told apart must not be served one answer");
    }

    [Fact]
    public async Task A_function_tool_is_still_keyed_by_its_name()
    {
        var inner = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        using var client = new TraconResponseCachingChatClient(inner, new FakeDistributedCache(), "openai", "tenant-a", Settings);
        var options = new ChatOptions { ModelId = "m", Tools = [TestData.Tool("lookup")] };

        await client.GetResponseAsync(Messages, options, TestContext.Current.CancellationToken);
        await client.GetResponseAsync(Messages, options, TestContext.Current.CancellationToken);

        inner.CallCount.ShouldBe(1);
    }
}
