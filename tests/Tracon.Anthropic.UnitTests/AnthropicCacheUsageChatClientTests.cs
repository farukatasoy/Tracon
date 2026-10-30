using System.Text.Json;
using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;
using Tracon.Anthropic.UnitTests.Infrastructure;

namespace Tracon.Anthropic.UnitTests;

/// <summary>
/// The ring that reads Anthropic's cache-write split: what it writes next to the adapter's
/// total, and how often it warns when the split is missing.
/// </summary>
public sealed class AnthropicCacheUsageChatClientTests
{
    private static readonly ChatMessage[] Messages = [new ChatMessage(ChatRole.User, "hi")];

    [Fact]
    public async Task A_missing_split_warns_once_per_instance_however_many_calls_follow()
    {
        // "Once" is per instance - per compiled agent - not per process; a second instance warns
        // again, and a test of "once" has to show both halves.
        using var logs = new RecordingLoggerProvider();
        var ring = Ring(null, logs);

        await ring.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken);
        await ring.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken);
        await ring.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken);

        Warnings(logs).Count.ShouldBe(1);

        await Ring(null, logs).GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken);

        Warnings(logs).Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_missing_split_writes_the_whole_total_under_the_class_the_binding_asks_for()
    {
        using var logs = new RecordingLoggerProvider();

        var shortWrite = await Ring(null, logs).GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken);
        var longWrite = await Ring("1h", logs).GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken);

        Counts(shortWrite)[TraconUsageCounts.CacheWriteInputTokens].ShouldBe(100);
        Counts(shortWrite).ContainsKey(TraconUsageCounts.CacheWriteLongInputTokens).ShouldBeFalse();
        Counts(longWrite)[TraconUsageCounts.CacheWriteLongInputTokens].ShouldBe(100);
        Counts(longWrite).ContainsKey(TraconUsageCounts.CacheWriteInputTokens).ShouldBeFalse();
    }

    [Fact]
    public async Task A_split_that_adds_up_to_less_than_the_total_hands_the_remainder_to_the_binding_class()
    {
        using var logs = new RecordingLoggerProvider();
        var raw = Message.FromRawUnchecked(new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["usage"] = JsonSerializer.SerializeToElement(new
            {
                cache_creation = new { ephemeral_5m_input_tokens = 30, ephemeral_1h_input_tokens = 50 },
            }),
        });

        var response = await Ring("1h", logs, raw).GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken);

        // 30 + 50 of the 100 are named; the other 20 go to the class the binding asks for (one hour).
        Counts(response)[TraconUsageCounts.CacheWriteInputTokens].ShouldBe(30);
        Counts(response)[TraconUsageCounts.CacheWriteLongInputTokens].ShouldBe(70);
        Warnings(logs).ShouldBeEmpty("a split that exists is not a missing one");
    }

    private static List<string> Warnings(RecordingLoggerProvider logs)
        => [.. logs.Entries.Where(static entry => entry.StartsWith("Warning ", StringComparison.Ordinal))];

    private static AdditionalPropertiesDictionary<long> Counts(ChatResponse response)
        => response.Usage.ShouldNotBeNull().AdditionalCounts.ShouldNotBeNull();

    private static AnthropicCacheUsageChatClient Ring(string? configuredTtl, RecordingLoggerProvider logs, object? raw = null)
        => new(new TotalOnlyClient(raw), configuredTtl, logs.CreateLogger("test"));

    /// <summary>Answers with the total the adapter reports (100 cache-write tokens) and nothing else.</summary>
    private sealed class TotalOnlyClient(object? raw) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
            {
                RawRepresentation = raw,
                Usage = new UsageDetails
                {
                    InputTokenCount = 120,
                    AdditionalCounts = new AdditionalPropertiesDictionary<long> { ["CacheCreationInputTokens"] = 100 },
                },
            });

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(System.Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
