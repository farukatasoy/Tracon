using Microsoft.Extensions.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// Verifies <see cref="TraconResponseCachingChatClient"/>'s entry
/// lifetime and its fail-open behavior when the backing
/// <c>IDistributedCache</c> itself fails.
/// </summary>
public sealed class ResponseCacheLifetimeTests
{
    private static readonly ChatMessage[] Messages = [new ChatMessage(ChatRole.User, "hi")];

    [Fact]
    public async Task Non_streaming_write_applies_the_configured_lifetime()
    {
        var cache = new FakeDistributedCache();
        var inner = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        var settings = new ResponseCacheSettings { Enabled = true, Lifetime = TimeSpan.FromMinutes(42) };

        using var client = new TraconResponseCachingChatClient(inner, cache, "openai", "tenant-a", settings);

        await client.GetResponseAsync(Messages, new ChatOptions { ModelId = "gpt-5" }, TestContext.Current.CancellationToken);

        cache.SetCount.ShouldBe(1);
        cache.LastSetOptions.ShouldNotBeNull();
        cache.LastSetOptions!.AbsoluteExpirationRelativeToNow.ShouldBe(TimeSpan.FromMinutes(42));
    }

    [Fact]
    public async Task Streaming_write_ALSO_applies_the_configured_lifetime()
    {
        // 🚨 Regression for "signature vs. body is two separate steps":
        // writing only WriteCacheAsync and forgetting WriteCacheStreamingAsync
        // would leave a streaming run's cache entry with no TTL. This test
        // forces the uncoalesced streaming write path
        // (CoalesceStreamingUpdates = false) so WriteCacheStreamingAsync is
        // the one actually exercised, not WriteCacheAsync via ToChatResponse().
        var cache = new FakeDistributedCache();
        var inner = new FakeChatClient(streamingUpdates: [new ChatResponseUpdate(ChatRole.Assistant, "chunk")]);
        var settings = new ResponseCacheSettings { Enabled = true, Lifetime = TimeSpan.FromMinutes(7) };

        using var client = new TraconResponseCachingChatClient(inner, cache, "openai", "tenant-a", settings)
        {
            CoalesceStreamingUpdates = false,
        };

        await foreach (var _ in client.GetStreamingResponseAsync(
            Messages, new ChatOptions { ModelId = "gpt-5" }, TestContext.Current.CancellationToken))
        {
            // Draining the stream is enough to trigger the cache write.
        }

        cache.SetCount.ShouldBe(1);
        cache.LastSetOptions.ShouldNotBeNull();
        cache.LastSetOptions!.AbsoluteExpirationRelativeToNow.ShouldBe(TimeSpan.FromMinutes(7));
    }

    [Fact]
    public async Task A_cache_read_failure_is_treated_as_a_miss_and_the_real_model_still_answers()
    {
        var cache = new FakeDistributedCache { ThrowOnGet = new InvalidOperationException("store unavailable") };
        var inner = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        var settings = new ResponseCacheSettings { Enabled = true, Lifetime = TimeSpan.FromMinutes(10) };

        using var client = new TraconResponseCachingChatClient(inner, cache, "openai", "tenant-a", settings);

        var response = await client.GetResponseAsync(
            Messages, new ChatOptions { ModelId = "gpt-5" }, TestContext.Current.CancellationToken);

        response.Text.ShouldBe("answer");
        inner.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_cache_write_failure_does_not_fail_the_call_that_already_succeeded()
    {
        // The risk this guards: an IDistributedCache implementation rejecting
        // an oversized payload (or a store outage) must not turn an
        // otherwise-successful model call into a failed run.
        var cache = new FakeDistributedCache { ThrowOnSet = new InvalidOperationException("payload too large") };
        var inner = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        var settings = new ResponseCacheSettings { Enabled = true, Lifetime = TimeSpan.FromMinutes(10) };

        using var client = new TraconResponseCachingChatClient(inner, cache, "openai", "tenant-a", settings);

        var response = await client.GetResponseAsync(
            Messages, new ChatOptions { ModelId = "gpt-5" }, TestContext.Current.CancellationToken);

        response.Text.ShouldBe("answer");
        cache.SetCount.ShouldBe(0);
    }

    [Fact]
    public async Task Cancellation_during_a_cache_write_propagates_as_a_cancellation_not_a_swallowed_failure()
    {
        // 🚨 Regression: a caller's OWN
        // cancellation is never mistaken for a store failure and
        // logged/swallowed - it must surface as a cancellation like any other
        // canceled call in this codebase. "Own" means the caller's token is
        // cancelled while the write is in flight.
        using var caller = new CancellationTokenSource();
        var cache = new FakeDistributedCache
        {
            ThrowOnSet = new OperationCanceledException(caller.Token),
            BeforeSet = caller.Cancel,
        };
        var inner = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        var settings = new ResponseCacheSettings { Enabled = true, Lifetime = TimeSpan.FromMinutes(10) };

        using var client = new TraconResponseCachingChatClient(inner, cache, "openai", "tenant-a", settings);

        await Should.ThrowAsync<OperationCanceledException>(() => client.GetResponseAsync(
            Messages, new ChatOptions { ModelId = "gpt-5" }, caller.Token));
    }

    [Fact]
    public async Task A_cache_that_times_out_on_its_own_is_a_miss_on_read_and_a_skipped_write()
    {
        // The caller is still there; a distributed cache's own client timeout
        // surfaced as TaskCanceledException. The cache is an optimization, not a
        // dependency: the model still answers.
        var cache = new FakeDistributedCache
        {
            ThrowOnGet = new TaskCanceledException("the cache timed out"),
            ThrowOnSet = new TaskCanceledException("the cache timed out"),
        };
        var inner = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        var settings = new ResponseCacheSettings { Enabled = true, Lifetime = TimeSpan.FromMinutes(10) };

        using var client = new TraconResponseCachingChatClient(inner, cache, "openai", "tenant-a", settings);

        var response = await client.GetResponseAsync(
            Messages, new ChatOptions { ModelId = "gpt-5" }, TestContext.Current.CancellationToken);

        response.Text.ShouldBe("answer");
        inner.CallCount.ShouldBe(1);
    }
}
