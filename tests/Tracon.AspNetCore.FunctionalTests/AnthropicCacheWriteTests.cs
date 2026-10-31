using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.NodeUsageHost;
using static Tracon.AspNetCore.FunctionalTests.OutputTruncationTests;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// An Anthropic cache write is priced as the fourth term of a run's cost, in every place that
/// decides about money.
/// </summary>
/// <remarks>
/// <para>
/// Through the whole host and the real SDK, against a local Messages API: only that path shows
/// what the adapter reports (the write tokens inside the input total, and only their total), where
/// the per-TTL split sits on the wire (the response's usage object; the <c>message_start</c> event
/// when streaming) and what the layers above do with it.
/// </para>
/// <para>
/// One scripted call spends 100 plain, 200 read and 900 written input tokens (600 short, 300 long)
/// and 10 output tokens. At 1000 per million for input, 2000 for output, 100 for reads, 1250 for
/// short writes and 2000 for long writes: <c>0.1 + 0.02 + 0.02 + 1.35 = 1.49</c>. A three-term sum
/// would give <c>0.14</c>: a tenth of the bill.
/// </para>
/// </remarks>
public sealed class AnthropicCacheWriteTests
{
    private const decimal CallCost = 1.49m;

    private const string SplitUsage = """
        {"input_tokens":100,"cache_creation_input_tokens":900,"cache_read_input_tokens":200,
         "cache_creation":{"ephemeral_5m_input_tokens":600,"ephemeral_1h_input_tokens":300},
         "output_tokens":10}
        """;

    private const string TotalOnlyUsage = """
        {"input_tokens":100,"cache_creation_input_tokens":900,"cache_read_input_tokens":200,"output_tokens":10}
        """;

    [Fact]
    public async Task A_call_that_wrote_to_the_cache_records_both_counters_and_prices_the_write_as_the_fourth_term()
    {
        await using var anthropic = await FakeAnthropicServer.StartAsync();
        anthropic.Respond(Message(SplitUsage));

        await using var host = await StartAsync(anthropic, Priced());

        using var response = await PostBufferedAsync(host);
        response.EnsureSuccessStatusCode();

        var run = await SingleRunAsync(host);

        AssertWritten(run);
    }

    [Fact]
    public async Task A_streamed_call_reads_the_split_from_the_message_start_event()
    {
        // The split is on message_start only; the message_delta event and the adapter's own usage carry the total.
        await using var anthropic = await FakeAnthropicServer.StartAsync();
        anthropic.Stream(Stream(SplitUsage));

        await using var host = await StartAsync(anthropic, Priced());

        await PostStreamingAsync(host);

        AssertWritten(await SingleRunAsync(host));
    }

    [Fact]
    public async Task The_write_counters_and_the_write_cost_reach_the_wire_of_the_run_endpoint()
    {
        await using var anthropic = await FakeAnthropicServer.StartAsync();
        anthropic.Respond(Message(SplitUsage));

        await using var host = await StartAsync(anthropic, Priced());

        using var response = await PostBufferedAsync(host);
        response.EnsureSuccessStatusCode();

        var run = await SingleRunAsync(host);

        using var wire = JsonDocument.Parse(await host.Client.GetStringAsync(
            new Uri($"/tracon/api/runs/{run.Id}", UriKind.Relative),
            TestContext.Current.CancellationToken));

        var usage = wire.RootElement.GetProperty("usage");
        var cost = wire.RootElement.GetProperty("cost");

        usage.GetProperty("cacheWriteInputTokens").GetInt64().ShouldBe(600);
        usage.GetProperty("cacheWriteLongInputTokens").GetInt64().ShouldBe(300);
        cost.GetProperty("cacheWriteCost").GetDecimal().ShouldBe(1.35m);
        cost.GetProperty("cacheWritePricePerMillionTokens").GetDecimal().ShouldBe(1_250m);
        cost.GetProperty("cacheWriteLongPricePerMillionTokens").GetDecimal().ShouldBe(2_000m);
    }

    [Fact]
    public async Task The_quota_counter_and_the_webhook_carry_the_whole_cost_including_the_write()
    {
        // the twin: a tenant's ceiling is read from this number. The write is 1.35 of the 1.49.
        var publisher = new RecordingPublisher();

        await using var anthropic = await FakeAnthropicServer.StartAsync();
        anthropic.Respond(Message(SplitUsage));

        await using var host = await StartAsync(anthropic, Priced(), publisher);

        using var response = await PostBufferedAsync(host);
        response.EnsureSuccessStatusCode();

        var run = await SingleRunAsync(host);

        (await DailyCounterAsync(host, AgentName)).Cost.ShouldBe(CallCost);

        var completed = publisher.Completed().ShouldHaveSingleItem();

        completed.Cost.ShouldBe(CallCost);
        completed.Currency.ShouldBe("USD");
        RunSpend.Total(run.Cost, run.NonTokenCost).ShouldBe(CallCost);
    }

    [Fact]
    public async Task The_statistics_endpoint_and_the_run_tree_total_include_the_write()
    {
        await using var anthropic = await FakeAnthropicServer.StartAsync();
        anthropic.Respond(Message(SplitUsage));

        await using var host = await StartAsync(anthropic, Priced());

        using var response = await PostBufferedAsync(host);
        response.EnsureSuccessStatusCode();

        var run = await SingleRunAsync(host);

        var statistics = await host.Services.GetRequiredService<IRunStore>().GetStatisticsAsync(new RunStatisticsQuery());

        statistics.TotalCost.ShouldBe(CallCost);
        statistics.CacheWriteInputTokens.ShouldBe(600);
        statistics.CacheWriteLongInputTokens.ShouldBe(300);
        statistics.CachedInputTokens.ShouldBe(200);
        statistics.InputTokens.ShouldBe(1_200);

        run.TreeCost.ShouldNotBeNull().Total().ShouldBe(CallCost);
    }

    [Theory]
    [InlineData("1h", 0L, 900L)]
    [InlineData("5m", 900L, 0L)]
    [InlineData(null, 900L, 0L)]
    public async Task Without_the_split_the_whole_total_goes_to_the_class_the_binding_asks_for(string? ttl, long expectedShort, long expectedLong)
    {
        await using var anthropic = await FakeAnthropicServer.StartAsync();
        anthropic.Respond(Message(TotalOnlyUsage));

        await using var host = await StartAsync(anthropic, Priced(), ttl: ttl);

        using var response = await PostBufferedAsync(host);
        response.EnsureSuccessStatusCode();

        var run = await SingleRunAsync(host);
        var usage = run.Usage.ShouldNotBeNull();

        usage.CacheWriteInputTokens.ShouldBe(expectedShort == 0 ? null : expectedShort);
        usage.CacheWriteLongInputTokens.ShouldBe(expectedLong == 0 ? null : expectedLong);
        usage.InputTokens.ShouldBe(1_200);

        host.Logs.AllText.ShouldContain("without the per-TTL split");
    }

    [Fact]
    public async Task Each_call_of_a_paused_turn_reports_its_own_split()
    {
        // The first call writes short, the continuation writes long; the run sums both. The ring reads each API call on its own.
        await using var anthropic = await FakeAnthropicServer.StartAsync();
        anthropic
            .Respond(Message(
                """{"input_tokens":10,"cache_creation_input_tokens":400,"cache_read_input_tokens":0,"cache_creation":{"ephemeral_5m_input_tokens":400,"ephemeral_1h_input_tokens":0},"output_tokens":5}""",
                stopReason: "pause_turn"))
            .Respond(Message(
                """{"input_tokens":10,"cache_creation_input_tokens":250,"cache_read_input_tokens":0,"cache_creation":{"ephemeral_5m_input_tokens":0,"ephemeral_1h_input_tokens":250},"output_tokens":5}"""));

        await using var host = await StartAsync(anthropic, Priced());

        using var response = await PostBufferedAsync(host);
        response.EnsureSuccessStatusCode();

        anthropic.Bodies.Count.ShouldBe(2);

        var usage = (await SingleRunAsync(host)).Usage.ShouldNotBeNull();

        usage.CacheWriteInputTokens.ShouldBe(400);
        usage.CacheWriteLongInputTokens.ShouldBe(250);
        usage.InputTokens.ShouldBe(670);
    }

    [Fact]
    public async Task Without_write_rates_the_cost_is_what_it_was_before_the_write_was_priced()
    {
        // The same call on a catalog that carries no write rate costs exactly the input price
        // for the written tokens, and the run records the counters all the same.
        await using var anthropic = await FakeAnthropicServer.StartAsync();
        anthropic.Respond(Message(SplitUsage));

        await using var host = await StartAsync(anthropic, Priced() with
        {
            CacheWriteCostPerMillionTokens = null,
            CacheWriteLongCostPerMillionTokens = null,
        });

        using var response = await PostBufferedAsync(host);
        response.EnsureSuccessStatusCode();

        var run = await SingleRunAsync(host);
        var cost = run.Cost.ShouldNotBeNull();

        // 1000 plain-or-written input tokens at 1000 per million, 10 output tokens at 2000, 200 reads at 100.
        cost.InputCost.ShouldBe(1.0m);
        cost.OutputCost.ShouldBe(0.02m);
        cost.CachedInputCost.ShouldBe(0.02m);
        cost.CacheWriteCost.ShouldBeNull();
        cost.CacheWritePricePerMillionTokens.ShouldBeNull();
        cost.CacheWriteLongPricePerMillionTokens.ShouldBeNull();
        cost.Total().ShouldBe(1.04m);
        cost.Source.ShouldBe(PricingSource.Catalog);

        run.Usage.ShouldNotBeNull().CacheWriteInputTokens.ShouldBe(600);
    }

    [Fact]
    public async Task A_call_that_only_read_the_cache_reports_no_write()
    {
        await using var anthropic = await FakeAnthropicServer.StartAsync();
        anthropic.Respond(Message(
            """{"input_tokens":100,"cache_creation_input_tokens":0,"cache_read_input_tokens":900,"cache_creation":{"ephemeral_5m_input_tokens":0,"ephemeral_1h_input_tokens":0},"output_tokens":10}"""));

        await using var host = await StartAsync(anthropic, Priced());

        using var response = await PostBufferedAsync(host);
        response.EnsureSuccessStatusCode();

        var run = await SingleRunAsync(host);

        run.Usage.ShouldNotBeNull().CacheWriteInputTokens.ShouldBeNull();
        run.Usage.CacheWriteLongInputTokens.ShouldBeNull();
        run.Usage.CachedInputTokens.ShouldBe(900);
        run.Cost.ShouldNotBeNull().CacheWriteCost.ShouldBeNull();

        host.Logs.AllText.ShouldNotContain("without the per-TTL split");
    }

    private static void AssertWritten(RunRecord run)
    {
        var usage = run.Usage.ShouldNotBeNull();

        // The written tokens are INSIDE the input total: 100 plain + 200 read + 900 written.
        usage.InputTokens.ShouldBe(1_200);
        usage.CachedInputTokens.ShouldBe(200);
        usage.CacheWriteInputTokens.ShouldBe(600);
        usage.CacheWriteLongInputTokens.ShouldBe(300);

        var cost = run.Cost.ShouldNotBeNull();

        cost.InputCost.ShouldBe(0.1m);
        cost.OutputCost.ShouldBe(0.02m);
        cost.CachedInputCost.ShouldBe(0.02m);
        cost.CacheWriteCost.ShouldBe(1.35m);
        cost.CacheWritePricePerMillionTokens.ShouldBe(1_250m);
        cost.CacheWriteLongPricePerMillionTokens.ShouldBe(2_000m);
        cost.Total().ShouldBe(CallCost);
    }

    private static ModelDescriptor Priced()
        => new()
        {
            Name = "claude-test",
            InputCostPerMillionTokens = 1_000m,
            OutputCostPerMillionTokens = 2_000m,
            CachedInputCostPerMillionTokens = 100m,
            CacheWriteCostPerMillionTokens = 1_250m,
            CacheWriteLongCostPerMillionTokens = 2_000m,
        };

    private static string Message(string usage, string stopReason = "end_turn")
        => $$"""
            {"id":"msg_cache","type":"message","role":"assistant","model":"claude-test",
             "content":[{"type":"text","text":"ok"}],
             "stop_reason":"{{stopReason}}","stop_sequence":null,
             "usage":{{usage}}}
            """;

    /// <summary>One streamed message as the API sends it: the split on message_start, the totals on message_delta.</summary>
    private static string Stream(string usage)
    {
        var start = JsonNode.Parse(usage)!.AsObject();
        start["output_tokens"] = 1;

        var delta = new JsonObject();

        foreach (var name in new[] { "input_tokens", "cache_creation_input_tokens", "cache_read_input_tokens", "output_tokens" })
        {
            delta[name] = JsonNode.Parse(usage)![name]!.DeepClone();
        }

        return string.Concat(
            Frame("message_start", new JsonObject
            {
                ["type"] = "message_start",
                ["message"] = new JsonObject
                {
                    ["id"] = "msg_cache",
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
                ["usage"] = delta,
            }),
            Frame("message_stop", new JsonObject { ["type"] = "message_stop" }));
    }

    private static string Frame(string name, JsonObject data) => $"event: {name}\ndata: {data.ToJsonString()}\n\n";

    private static Task<TraconTestHost> StartAsync(
        FakeAnthropicServer anthropic,
        ModelDescriptor model,
        RecordingPublisher? publisher = null,
        string? ttl = "5m")
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .UseAnthropic("test-anthropic-cache-write", options =>
                {
                    options.Endpoint = anthropic.BaseAddress;
                    options.MaxRetries = 0;
                    options.Models.Add(model);
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
                        ProviderSettings = ttl is null
                            ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                            {
                                [AnthropicProviderNames.PromptCachingSetting] = JsonSerializer.SerializeToElement(true),
                            }
                            : new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                            {
                                [AnthropicProviderNames.PromptCachingSetting] = JsonSerializer.SerializeToElement(true),
                                [AnthropicProviderNames.PromptCachingTtlSetting] = JsonSerializer.SerializeToElement(ttl),
                            },
                    },
                    Origin = AgentDefinitionOrigin.Code,
                }),
            configureServices: services =>
            {
                services.Configure<TraconOptions>(options => options.Pricing.Currency = "USD");

                if (publisher is not null)
                {
                    services.AddSingleton<IWebhookPublisher>(publisher);
                }
            });

    private sealed class RecordingPublisher : IWebhookPublisher
    {
        private readonly List<(string EventType, WebhookEventPayload Payload)> _events = [];

        public IReadOnlyList<WebhookRunSummary> Completed()
        {
            lock (_events)
            {
                return
                [
                    .. _events
                        .Where(static entry => string.Equals(entry.EventType, WebhookEvents.RunCompleted, StringComparison.Ordinal))
                        .Select(static entry => entry.Payload.Run.ShouldNotBeNull()),
                ];
            }
        }

        public ValueTask<int> PublishAsync(
            string tenantId,
            string eventType,
            WebhookEventPayload payload,
            CancellationToken cancellationToken = default)
        {
            lock (_events)
            {
                _events.Add((eventType, payload));
            }

            return new ValueTask<int>(1);
        }
    }
}
