using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Models;

/// <summary>
/// Verifies <see cref="AgentPrismResponseCachingChatClient"/>'s cache key:
/// tenant, tool set and provider must each separate an entry, and the key
/// must be stable enough that two SEPARATE pipeline instances (the shape a
/// real distributed cache is used in) still share a hit.
/// </summary>
/// <remarks>
/// Section 81.2's measured gap: MEAI's own <c>DistributedCachingChatClient.GetCacheKey</c>
/// does not separate by <see cref="ChatOptions.Tools"/> or by tenant. Every
/// "does NOT share" test here fails if <see cref="AgentPrismResponseCachingChatClient.GetCacheKey"/>
/// were reduced back to <c>base.GetCacheKey(...)</c> alone.
/// </remarks>
public sealed class ResponseCacheKeyTests
{
    private static readonly ChatMessage[] Messages = [new ChatMessage(ChatRole.User, "What is 2+2?")];

    private static readonly ResponseCacheSettings Settings = new() { Enabled = true, Lifetime = TimeSpan.FromMinutes(10) };

    private static AIFunction Tool(string name) => AIFunctionFactory.Create((string _) => "ok", name);

    [Fact]
    public async Task Same_request_through_one_instance_is_a_cache_hit()
    {
        var cache = new FakeDistributedCache();
        var inner = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "4")));

        using var client = new AgentPrismResponseCachingChatClient(inner, cache, "openai", "tenant-a", Settings);
        var options = new ChatOptions { ModelId = "gpt-5", Tools = [Tool("calc")] };

        await client.GetResponseAsync(Messages, options, TestContext.Current.CancellationToken);
        await client.GetResponseAsync(Messages, options, TestContext.Current.CancellationToken);

        inner.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Same_tenant_tool_set_and_prompt_hits_across_two_separate_pipeline_instances()
    {
        // The realistic shape: ModelProviderRegistry builds a FRESH pipeline
        // (and a fresh AgentPrismResponseCachingChatClient) per compiled
        // agent. Only the backing IDistributedCache is shared between them.
        var cache = new FakeDistributedCache();
        var innerA = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "4")));
        var innerB = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "4")));

        using var clientA = new AgentPrismResponseCachingChatClient(innerA, cache, "openai", "tenant-a", Settings);
        using var clientB = new AgentPrismResponseCachingChatClient(innerB, cache, "openai", "tenant-a", Settings);

        var options = new ChatOptions { ModelId = "gpt-5", Tools = [Tool("calc")] };

        await clientA.GetResponseAsync(Messages, options, TestContext.Current.CancellationToken);
        await clientB.GetResponseAsync(Messages, options, TestContext.Current.CancellationToken);

        innerA.CallCount.ShouldBe(1);
        innerB.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Different_tool_set_does_not_share_a_cache_entry()
    {
        var cache = new FakeDistributedCache();
        var innerA = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "4")));
        var innerB = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "4")));

        using var clientA = new AgentPrismResponseCachingChatClient(innerA, cache, "openai", "tenant-a", Settings);
        using var clientB = new AgentPrismResponseCachingChatClient(innerB, cache, "openai", "tenant-a", Settings);

        // Same tenant, same provider, same instructions/messages/model id -
        // the base MEAI key would be IDENTICAL. Only the tool set differs.
        var optionsA = new ChatOptions { ModelId = "gpt-5", Tools = [Tool("calc")] };
        var optionsB = new ChatOptions { ModelId = "gpt-5", Tools = [Tool("send_email")] };

        await clientA.GetResponseAsync(Messages, optionsA, TestContext.Current.CancellationToken);
        await clientB.GetResponseAsync(Messages, optionsB, TestContext.Current.CancellationToken);

        innerA.CallCount.ShouldBe(1);

        // If GetCacheKey ignored Tools (the measured gap), B would hit A's
        // entry here and never call its own inner client.
        innerB.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Different_tenant_does_not_share_a_cache_entry()
    {
        var cache = new FakeDistributedCache();
        var innerA = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "4")));
        var innerB = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "4")));

        using var clientA = new AgentPrismResponseCachingChatClient(innerA, cache, "openai", "tenant-a", Settings);
        using var clientB = new AgentPrismResponseCachingChatClient(innerB, cache, "openai", "tenant-b", Settings);

        var options = new ChatOptions { ModelId = "gpt-5", Tools = [Tool("calc")] };

        await clientA.GetResponseAsync(Messages, options, TestContext.Current.CancellationToken);
        await clientB.GetResponseAsync(Messages, options, TestContext.Current.CancellationToken);

        innerA.CallCount.ShouldBe(1);
        innerB.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Different_provider_does_not_share_a_cache_entry()
    {
        // Same model NAME can be reused by an OpenAI-compatible endpoint
        // under a different provider (81.2's third additional input).
        var cache = new FakeDistributedCache();
        var innerA = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "4")));
        var innerB = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "4")));

        using var clientA = new AgentPrismResponseCachingChatClient(innerA, cache, "openai", "tenant-a", Settings);
        using var clientB = new AgentPrismResponseCachingChatClient(innerB, cache, "openai-compatible", "tenant-a", Settings);

        var options = new ChatOptions { ModelId = "gpt-5", Tools = [Tool("calc")] };

        await clientA.GetResponseAsync(Messages, options, TestContext.Current.CancellationToken);
        await clientB.GetResponseAsync(Messages, options, TestContext.Current.CancellationToken);

        innerA.CallCount.ShouldBe(1);
        innerB.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Empty_message_list_does_not_throw()
    {
        var cache = new FakeDistributedCache();
        var inner = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "hi")));

        using var client = new AgentPrismResponseCachingChatClient(inner, cache, "openai", "tenant-a", Settings);

        var response = await client.GetResponseAsync([], new ChatOptions { ModelId = "gpt-5" }, TestContext.Current.CancellationToken);

        response.Text.ShouldBe("hi");
    }
}
