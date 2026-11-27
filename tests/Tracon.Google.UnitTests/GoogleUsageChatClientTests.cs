using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Tracon.Google.UnitTests.Infrastructure;
using Tracon.ProviderCore.Tests;

namespace Tracon.Google.UnitTests;

/// <summary>
/// The output tokens a Gemini call reports include its thinking tokens, as they do for the other providers, so the
/// output count is the one the service bills.
/// </summary>
/// <remarks>
/// <para>
/// Gemini reports the answer in <c>candidatesTokenCount</c> and the thinking in <c>thoughtsTokenCount</c>, two counts
/// that do not overlap, and it bills the thinking at the output price. The adapter copies them as they are: the output
/// count holds the answer and the reasoning count holds the thinking. Every consumer of the usage downstream (the cost,
/// the budget, the quota, the metrics, the run screen) reads the reasoning count as a part of the output count, which is
/// what OpenAI and Anthropic report. A call with 26 answer tokens and 319 thinking tokens was priced as 26.
/// </para>
/// <para>
/// The fix sits at the provider boundary: the client adds the thinking to the output count and leaves the reasoning and
/// the total as the service gave them. The wire tests read the usage the real adapter builds from a real answer body.
/// </para>
/// </remarks>
public sealed class GoogleUsageChatClientTests
{
    private const string Usage =
        """{"promptTokenCount":10,"candidatesTokenCount":26,"thoughtsTokenCount":319,"totalTokenCount":355}""";

    [Fact]
    public async Task A_buffered_call_counts_its_thinking_inside_the_output()
    {
        using var client = new GoogleUsageChatClient(new Scripted(Details(output: 26, reasoning: 319)));

        var usage = (await client.GetResponseAsync("hi", cancellationToken: TestContext.Current.CancellationToken)).Usage.ShouldNotBeNull();

        usage.InputTokenCount.ShouldBe(10);
        usage.OutputTokenCount.ShouldBe(345, "the answer and the thinking are both output");
        usage.ReasoningTokenCount.ShouldBe(319, "the reasoning count stays what the service reported");
        usage.TotalTokenCount.ShouldBe(355, "the total already held both");
        usage.CachedInputTokenCount.ShouldBe(4);
        usage.AdditionalCounts.ShouldNotBeNull()["ToolUsePromptTokenCount"].ShouldBe(7);
    }

    [Fact]
    public async Task A_streamed_call_counts_its_thinking_inside_the_output_of_its_usage_update()
    {
        using var client = new GoogleUsageChatClient(new Scripted(Details(output: 26, reasoning: 319)));
        var updates = new List<ChatResponseUpdate>();

        await foreach (var update in client.GetStreamingResponseAsync("hi", cancellationToken: TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        updates.Select(static update => update.Text).Where(static text => text.Length > 0).ShouldBe(["part one", "part two"]);

        var usage = updates.SelectMany(static update => update.Contents).OfType<UsageContent>().ShouldHaveSingleItem().Details;

        usage.OutputTokenCount.ShouldBe(345);
        usage.ReasoningTokenCount.ShouldBe(319);
        usage.TotalTokenCount.ShouldBe(355);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    public async Task A_call_that_reports_no_thinking_keeps_its_output_as_reported(long? reasoning)
    {
        using var client = new GoogleUsageChatClient(new Scripted(Details(output: 26, reasoning: reasoning)));

        var usage = (await client.GetResponseAsync("hi", cancellationToken: TestContext.Current.CancellationToken)).Usage.ShouldNotBeNull();

        usage.OutputTokenCount.ShouldBe(26);
        usage.ReasoningTokenCount.ShouldBe(reasoning);
    }

    [Fact]
    public async Task A_call_that_reports_thinking_and_no_output_counts_the_thinking_as_the_output()
    {
        // A model that spends the whole limit on thinking answers with no candidate tokens at all.
        using var client = new GoogleUsageChatClient(new Scripted(Details(output: null, reasoning: 319)));

        var usage = (await client.GetResponseAsync("hi", cancellationToken: TestContext.Current.CancellationToken)).Usage.ShouldNotBeNull();

        usage.OutputTokenCount.ShouldBe(319);
        usage.ReasoningTokenCount.ShouldBe(319);
    }

    [Fact]
    public async Task A_call_without_usage_passes_through()
    {
        using var client = new GoogleUsageChatClient(new Scripted(details: null));

        var response = await client.GetResponseAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

        response.Usage.ShouldBeNull();
        response.Text.ShouldBe("answer");
    }

    [Fact]
    public async Task A_real_buffered_answer_is_counted_with_its_thinking_in_the_output()
    {
        await using var server = StubHttpServer.Respond(200, "OK", Answer(Usage));
        using var factory = new GoogleChatClientFactory(TestData.Options(options => options.Endpoint = server.BaseAddress));
        using var client = factory.CreateChatClient(TestData.Binding());

        var usage = (await client.GetResponseAsync("hi", cancellationToken: TestContext.Current.CancellationToken)).Usage.ShouldNotBeNull();

        usage.OutputTokenCount.ShouldBe(345);
        usage.ReasoningTokenCount.ShouldBe(319);
        usage.TotalTokenCount.ShouldBe(355);
    }

    [Fact]
    public async Task A_real_streamed_answer_is_counted_with_its_thinking_in_the_output()
    {
        await using var server = StubHttpServer.Respond(200, "OK", "data: " + Answer(Usage).ReplaceLineEndings(string.Empty) + "\r\n\r\n");
        using var factory = new GoogleChatClientFactory(TestData.Options(options => options.Endpoint = server.BaseAddress));
        using var client = factory.CreateChatClient(TestData.Binding());
        var updates = new List<ChatResponseUpdate>();

        await foreach (var update in client.GetStreamingResponseAsync("hi", cancellationToken: TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        var usage = updates.SelectMany(static update => update.Contents).OfType<UsageContent>().ShouldHaveSingleItem().Details;

        usage.OutputTokenCount.ShouldBe(345);
        usage.ReasoningTokenCount.ShouldBe(319);
    }

    [Fact]
    public async Task The_real_adapter_folds_the_cumulative_usage_of_every_chunk_of_a_stream_into_one_usage_update_with_the_last_numbers()
    {
        // A Gemini stream carries usageMetadata on several chunks, and each one holds the totals so far. Core sums every
        // usage update it sees, so the adapter has to hand over one: this pins what Google.GenAI does, and the first
        // assertion fails if a later SDK version starts to hand over one per chunk.
        await using var server = StubHttpServer.Respond(200, "OK", CumulativeStream);
        using var factory = new GoogleChatClientFactory(TestData.Options(options => options.Endpoint = server.BaseAddress));
        using var raw = factory.Client.AsIChatClient(TestData.Model);
        var updates = new List<ChatResponseUpdate>();

        await foreach (var update in raw.GetStreamingResponseAsync("hi", cancellationToken: TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        updates.Count(static update => update.Contents.OfType<TextContent>().Any()).ShouldBe(3, "three chunks of text came out");

        var usage = updates.SelectMany(static update => update.Contents).OfType<UsageContent>().ShouldHaveSingleItem().Details;

        usage.InputTokenCount.ShouldBe(10);
        usage.OutputTokenCount.ShouldBe(20, "the last chunk's candidates count, as the service reported it");
        usage.ReasoningTokenCount.ShouldBe(150);
        usage.TotalTokenCount.ShouldBe(180);
    }

    [Fact]
    public async Task A_stream_with_cumulative_usage_on_every_chunk_is_counted_once_with_the_thinking_of_its_last_chunk()
    {
        await using var server = StubHttpServer.Respond(200, "OK", CumulativeStream);
        using var factory = new GoogleChatClientFactory(TestData.Options(options => options.Endpoint = server.BaseAddress));
        using var client = factory.CreateChatClient(TestData.Binding());
        var updates = new List<ChatResponseUpdate>();

        await foreach (var update in client.GetStreamingResponseAsync("hi", cancellationToken: TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        var usage = updates.SelectMany(static update => update.Contents).OfType<UsageContent>().ShouldHaveSingleItem().Details;

        usage.OutputTokenCount.ShouldBe(170, "20 answer tokens and 150 thinking tokens, counted once and not once per chunk");
        usage.ReasoningTokenCount.ShouldBe(150);
        usage.TotalTokenCount.ShouldBe(180);

        // What Core does with the stream: it adds up every usage update.
        var summed = new UsageDetails();

        foreach (var content in updates.SelectMany(static update => update.Contents).OfType<UsageContent>())
        {
            summed.Add(content.Details);
        }

        summed.OutputTokenCount.ShouldBe(170);
    }

    /// <summary>Three chunks of one answer, each carrying the usage so far, as a Gemini stream does.</summary>
    private static readonly string CumulativeStream = string.Concat(
        Chunk("one ", """{"promptTokenCount":10,"candidatesTokenCount":5,"totalTokenCount":15}"""),
        Chunk("two ", """{"promptTokenCount":10,"candidatesTokenCount":10,"thoughtsTokenCount":100,"totalTokenCount":120}"""),
        Chunk("three", """{"promptTokenCount":10,"candidatesTokenCount":20,"thoughtsTokenCount":150,"totalTokenCount":180}""", finished: true));

    private static string Chunk(string text, string usage, bool finished = false)
    {
        var finish = finished ? ",\"finishReason\":\"STOP\"" : string.Empty;

        return "data: {\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":\"" + text + "\"}]}" + finish + "}],"
               + "\"usageMetadata\":" + usage + ",\"modelVersion\":\"gemini-test\"}\r\n\r\n";
    }

    private static UsageDetails Details(long? output, long? reasoning)
        => new()
        {
            InputTokenCount = 10,
            CachedInputTokenCount = 4,
            OutputTokenCount = output,
            ReasoningTokenCount = reasoning,
            TotalTokenCount = 355,
            AdditionalCounts = new AdditionalPropertiesDictionary<long> { ["ToolUsePromptTokenCount"] = 7 },
        };

    private static string Answer(string usage)
        => $$"""
            {"candidates":[{"content":{"role":"model","parts":[{"text":"ok"}]},"finishReason":"STOP"}],
             "usageMetadata":{{usage}},"modelVersion":"gemini-test"}
            """;

    /// <summary>An inner client that answers with the usage it is given, the way the adapter does: on the response, and as a last update.</summary>
    private sealed class Scripted(UsageDetails? details) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")) { Usage = details });

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();

            yield return new ChatResponseUpdate(ChatRole.Assistant, "part one");
            yield return new ChatResponseUpdate(ChatRole.Assistant, "part two");

            if (details is not null)
            {
                yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new UsageContent(details)] };
            }
        }

        public object? GetService(System.Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // The scripted client has nothing to release.
        }
    }
}
