using Microsoft.Extensions.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// Verifies that a response-cache HIT never re-reports the token usage the
/// ORIGINAL (cached) call carried.
/// </summary>
/// <remarks>
/// 🚨 Regression for a real defect measured against a live OpenAI call
/// (<c>samples/Tracon.Api</c>, closure): the stored
/// <see cref="ChatResponse"/> embeds the original call's
/// <see cref="ChatResponse.Usage"/> and <see cref="UsageContent"/>. Serving
/// them again on every subsequent hit made a run's own <c>usage</c> field
/// show the SAME non-zero token count as the first, real call — double
/// counting tokens/cost the provider was never billed for again. The
/// entire "a hit spends nothing" claim depends on this.
/// </remarks>
public sealed class ResponseCacheUsageTests
{
    private static readonly ChatMessage[] Messages = [new ChatMessage(ChatRole.User, "hi")];

    private static readonly ResponseCacheSettings Settings = new() { Enabled = true, Lifetime = TimeSpan.FromMinutes(10) };

    private static ChatResponse BuildResponseWithUsage()
    {
        var message = new ChatMessage(ChatRole.Assistant, "answer");
        message.Contents.Add(new UsageContent(new UsageDetails { InputTokenCount = 100, OutputTokenCount = 20, TotalTokenCount = 120 }));

        return new ChatResponse(message)
        {
            Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 20, TotalTokenCount = 120 },
        };
    }

    [Fact]
    public async Task A_cache_whose_read_fails_is_counted_as_an_error_and_not_as_a_miss()
    {
        using var meterFactory = new TestMeterFactory();
        using var collector = new MetricCollector(meterFactory.Meter);
        var metrics = new TraconMetrics(meterFactory);
        var cache = new FakeDistributedCache { ThrowOnGet = new InvalidOperationException("store unavailable") };
        var inner = new FakeChatClient(_ => BuildResponseWithUsage());

        using var client = new TraconResponseCachingChatClient(inner, cache, "openai", "tenant-a", Settings, metrics);
        var options = new ChatOptions { ModelId = "gpt-5" };

        // Non-streaming and streaming both read the store: the read fails, the call goes on to the provider.
        await client.GetResponseAsync(Messages, options, TestContext.Current.CancellationToken);
        await foreach (var update in client.GetStreamingResponseAsync(Messages, options, TestContext.Current.CancellationToken))
        {
            _ = update;
        }

        // The store is back: the first call wrote its answer, so the same request is a hit, another one a miss.
        cache.ThrowOnGet = null;
        await client.GetResponseAsync(Messages, options, TestContext.Current.CancellationToken);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "another question")], options, TestContext.Current.CancellationToken);

        collector.LongTags(TraconDiagnostics.ModelCacheLookupCounterName)
            .Select(static tags => (string?)tags[TraconDiagnostics.Tags.CacheResult])
            .ShouldBe(["error", "error", "hit", "miss"]);
    }

    [Fact]
    public async Task Cache_hit_strips_both_the_aggregate_usage_and_the_per_message_usage_content()
    {
        var cache = new FakeDistributedCache();
        var inner = new FakeChatClient(_ => BuildResponseWithUsage());

        using var client = new TraconResponseCachingChatClient(inner, cache, "openai", "tenant-a", Settings);
        var options = new ChatOptions { ModelId = "gpt-5" };

        var first = await client.GetResponseAsync(Messages, options, TestContext.Current.CancellationToken);

        // The real (miss) call is billed and MUST show real usage.
        first.Usage.ShouldNotBeNull();
        first.Usage!.TotalTokenCount.ShouldBe(120);

        var second = await client.GetResponseAsync(Messages, options, TestContext.Current.CancellationToken);

        inner.CallCount.ShouldBe(1); // confirms this really was a hit, not a second real call

        second.Usage.ShouldBeNull();
        second.Messages.SelectMany(static m => m.Contents).OfType<UsageContent>().ShouldBeEmpty();

        // The hit still carries the actual answer - only usage is stripped.
        second.Text.ShouldBe("answer");
    }

    [Fact]
    public async Task Streaming_cache_hit_ALSO_strips_per_frame_usage_content()
    {
        var cache = new FakeDistributedCache();

        var updatesWithUsage = new List<ChatResponseUpdate>
        {
            new(ChatRole.Assistant, "answer"),
            new(ChatRole.Assistant, [new UsageContent(new UsageDetails { InputTokenCount = 50, OutputTokenCount = 10, TotalTokenCount = 60 })]),
        };

        var inner = new FakeChatClient(streamingUpdates: updatesWithUsage);

        using var client = new TraconResponseCachingChatClient(inner, cache, "openai", "tenant-a", Settings)
        {
            CoalesceStreamingUpdates = false,
        };

        var options = new ChatOptions { ModelId = "gpt-5" };

        async Task<List<AIContent>> DrainAsync()
        {
            var contents = new List<AIContent>();

            await foreach (var update in client.GetStreamingResponseAsync(Messages, options, TestContext.Current.CancellationToken))
            {
                contents.AddRange(update.Contents);
            }

            return contents;
        }

        var firstContents = await DrainAsync();
        firstContents.OfType<UsageContent>().ShouldHaveSingleItem();

        var secondContents = await DrainAsync();

        inner.CallCount.ShouldBe(1); // confirms this really was a hit
        secondContents.OfType<UsageContent>().ShouldBeEmpty();
    }
}
