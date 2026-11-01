using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// A token count below zero never gets past the ring that sits right above the provider's client.
/// </summary>
/// <remarks>
/// A price times a negative count is a negative cost, and the quota, the budget and the cost total all read a negative
/// cost as money handed back. A count is unknown (<see langword="null"/>) or a number of tokens.
/// </remarks>
public sealed class UsageSanitizingChatClientTests
{
    [Fact]
    public async Task A_buffered_response_keeps_its_counts_and_loses_the_ones_below_zero()
    {
        var client = new UsageSanitizingChatClient(new StubClient(Usage(input: -5, output: 3, total: -2, cached: -1, reasoning: 0)));

        var response = await client.GetResponseAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        var usage = response.Usage.ShouldNotBeNull();
        usage.InputTokenCount.ShouldBeNull();
        usage.OutputTokenCount.ShouldBe(3);
        usage.TotalTokenCount.ShouldBeNull();
        usage.CachedInputTokenCount.ShouldBeNull();
        usage.ReasoningTokenCount.ShouldBe(0, "a measured zero is still a measurement");
    }

    [Fact]
    public async Task A_streamed_usage_update_is_cleaned_the_same_way()
    {
        var client = new UsageSanitizingChatClient(new StubClient(Usage(input: 7, output: -3, total: 4)));

        var updates = await client.GetStreamingResponseAsync("hello", cancellationToken: TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        var usage = updates.SelectMany(static update => update.Contents).OfType<UsageContent>().ShouldHaveSingleItem().Details;
        usage.InputTokenCount.ShouldBe(7);
        usage.OutputTokenCount.ShouldBeNull();
        usage.TotalTokenCount.ShouldBe(4);
    }

    [Fact]
    public void A_provider_specific_count_below_zero_is_removed_and_the_others_stay()
    {
        var usage = new UsageDetails
        {
            AdditionalCounts = new AdditionalPropertiesDictionary<long>
            {
                ["cache_creation_tokens"] = -9,
                ["audio_tokens"] = 12,
            },
        };

        UsageSanitizingChatClient.Sanitize(usage);

        usage.AdditionalCounts.ShouldNotBeNull().ShouldContainKey("audio_tokens");
        usage.AdditionalCounts.ShouldNotContainKey("cache_creation_tokens");
    }

    [Fact]
    public void A_response_without_usage_is_left_alone()
    {
        Should.NotThrow(static () => UsageSanitizingChatClient.Sanitize(null));
    }

    private static UsageDetails Usage(long? input = null, long? output = null, long? total = null, long? cached = null, long? reasoning = null)
        => new()
        {
            InputTokenCount = input,
            OutputTokenCount = output,
            TotalTokenCount = total,
            CachedInputTokenCount = cached,
            ReasoningTokenCount = reasoning,
        };

    private sealed class StubClient(UsageDetails usage) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")) { Usage = usage });

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
            yield return new ChatResponseUpdate { Contents = [new UsageContent(usage)] };

            await Task.CompletedTask.ConfigureAwait(false);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
