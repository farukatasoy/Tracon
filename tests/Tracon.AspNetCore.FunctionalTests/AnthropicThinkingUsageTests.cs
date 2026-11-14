using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.OutputTruncationTests;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The thinking tokens an Anthropic call reports reach the run's usage as reasoning tokens, and nothing else
/// in the usage moves.
/// </summary>
/// <remarks>
/// <para>
/// Through the whole host and the real SDK, against a local Messages API. Anthropic SDK 13.8 reads
/// <c>output_tokens_details.thinking_tokens</c> of the response (and of the <c>message_start</c> and
/// <c>message_delta</c> events when streaming) into the adapter's reasoning counter; 13.3 left that counter empty.
/// Tracon records the counter and does not price it, so the output tokens, and with them the cost, have to stay
/// what the API reported: a reasoning counter that were added to the output total would bill the thinking twice.
/// At 1000 per million for input and 2000 for output the call costs <c>0.04 + 0.42 = 0.46</c>.
/// </para>
/// <para>
/// One scripted call reports 210 output tokens, 150 of them thinking.
/// </para>
/// </remarks>
public sealed class AnthropicThinkingUsageTests
{
    private const string Usage = """
        {"input_tokens":40,"output_tokens":210,"output_tokens_details":{"thinking_tokens":150}}
        """;

    [Fact]
    public async Task A_buffered_call_records_the_thinking_tokens_as_reasoning_and_leaves_the_output_total_as_reported()
    {
        await using var anthropic = await FakeAnthropicServer.StartAsync();
        anthropic.Respond(Message(Usage));

        await using var host = await StartAsync(anthropic);

        using var response = await PostBufferedAsync(host);
        response.EnsureSuccessStatusCode();

        AssertThinking(await SingleRunAsync(host));
    }

    [Fact]
    public async Task A_streamed_call_records_the_thinking_tokens_of_its_message_delta()
    {
        await using var anthropic = await FakeAnthropicServer.StartAsync();
        anthropic.Stream(Stream(Usage));

        await using var host = await StartAsync(anthropic);

        await PostStreamingAsync(host);

        AssertThinking(await SingleRunAsync(host));
    }

    [Fact]
    public async Task A_call_without_thinking_reports_no_reasoning_counter()
    {
        await using var anthropic = await FakeAnthropicServer.StartAsync();
        anthropic.Respond(Message("""{"input_tokens":40,"output_tokens":60}"""));

        await using var host = await StartAsync(anthropic);

        using var response = await PostBufferedAsync(host);
        response.EnsureSuccessStatusCode();

        var usage = (await SingleRunAsync(host)).Usage.ShouldNotBeNull();

        usage.OutputTokens.ShouldBe(60);
        usage.ReasoningTokens.ShouldBeNull("a call that reports no thinking tokens has no reasoning counter, and zero would say it measured none");
    }

    [Fact]
    public async Task The_reasoning_counter_reaches_the_wire_of_the_run_endpoint()
    {
        await using var anthropic = await FakeAnthropicServer.StartAsync();
        anthropic.Respond(Message(Usage));

        await using var host = await StartAsync(anthropic);

        using var response = await PostBufferedAsync(host);
        response.EnsureSuccessStatusCode();

        var run = await SingleRunAsync(host);

        using var wire = JsonDocument.Parse(await host.Client.GetStringAsync(
            new Uri($"/tracon/api/runs/{run.Id}", UriKind.Relative),
            TestContext.Current.CancellationToken));

        var usage = wire.RootElement.GetProperty("usage");

        usage.GetProperty("reasoningTokens").GetInt64().ShouldBe(150);
        usage.GetProperty("outputTokens").GetInt64().ShouldBe(210);
    }

    private static void AssertThinking(RunRecord run)
    {
        var usage = run.Usage.ShouldNotBeNull();

        usage.InputTokens.ShouldBe(40);
        usage.OutputTokens.ShouldBe(210, "the output total is what the API reported; the thinking is a part of it, not an addition");
        usage.ReasoningTokens.ShouldBe(150);

        // The cost is the output price of the 210 tokens the API reported and the input price of the 40: the thinking tokens
        // are inside the 210 and are not priced a second time. A cost of 0.76 would mean they were.
        var cost = run.Cost.ShouldNotBeNull();

        cost.InputCost.ShouldBe(0.04m);
        cost.OutputCost.ShouldBe(0.42m);
        cost.Total().ShouldBe(0.46m);
    }

    private static string Message(string usage)
        => $$"""
            {"id":"msg_think","type":"message","role":"assistant","model":"claude-test",
             "content":[{"type":"text","text":"ok"}],
             "stop_reason":"end_turn","stop_sequence":null,
             "usage":{{usage}}}
            """;

    /// <summary>One streamed message as the API sends it: the input on message_start, the totals and the thinking on message_delta.</summary>
    private static string Stream(string usage)
    {
        var totals = JsonNode.Parse(usage)!.AsObject();

        var start = new JsonObject { ["input_tokens"] = totals["input_tokens"]!.DeepClone(), ["output_tokens"] = 1 };

        return string.Concat(
            Frame("message_start", new JsonObject
            {
                ["type"] = "message_start",
                ["message"] = new JsonObject
                {
                    ["id"] = "msg_think",
                    ["type"] = "message",
                    ["role"] = "assistant",
                    ["model"] = "claude-test",
                    ["content"] = new JsonArray(),
                    ["stop_reason"] = null,
                    ["stop_sequence"] = null,
                    ["usage"] = start,
                },
            }),
            Frame("content_block_start", new JsonObject
            {
                ["type"] = "content_block_start",
                ["index"] = 0,
                ["content_block"] = new JsonObject { ["type"] = "text", ["text"] = string.Empty },
            }),
            Frame("content_block_delta", new JsonObject
            {
                ["type"] = "content_block_delta",
                ["index"] = 0,
                ["delta"] = new JsonObject { ["type"] = "text_delta", ["text"] = "ok" },
            }),
            Frame("content_block_stop", new JsonObject { ["type"] = "content_block_stop", ["index"] = 0 }),
            Frame("message_delta", new JsonObject
            {
                ["type"] = "message_delta",
                ["delta"] = new JsonObject { ["stop_reason"] = "end_turn", ["stop_sequence"] = null },
                ["usage"] = totals,
            }),
            Frame("message_stop", new JsonObject { ["type"] = "message_stop" }));
    }

    private static string Frame(string name, JsonObject data) => $"event: {name}\ndata: {data.ToJsonString()}\n\n";

    private static Task<TraconTestHost> StartAsync(FakeAnthropicServer anthropic)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .UseAnthropic("test-anthropic-thinking", options =>
                {
                    options.Endpoint = anthropic.BaseAddress;
                    options.MaxRetries = 0;
                    options.Models.Add(new ModelDescriptor
                    {
                        Name = "claude-test",
                        InputCostPerMillionTokens = 1_000m,
                        OutputCostPerMillionTokens = 2_000m,
                    });
                })
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Reply briefly.",
                    Model = new ModelBinding
                    {
                        Provider = AnthropicProviderNames.Anthropic,
                        Model = "claude-test",
                        MaxOutputTokens = 1_000,
                    },
                    Origin = AgentDefinitionOrigin.Code,
                }),
            configureServices: services => services.Configure<TraconOptions>(options => options.Pricing.Currency = "USD"));
}
