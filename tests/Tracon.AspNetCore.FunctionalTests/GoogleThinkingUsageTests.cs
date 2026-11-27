using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.OutputTruncationTests;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The thinking tokens a Gemini call reports are part of the output tokens the run records, so the output cost is the
/// one the provider bills.
/// </summary>
/// <remarks>
/// <para>
/// Through the whole host and the real Google client, against a local Gemini endpoint. Gemini reports the answer in
/// <c>candidatesTokenCount</c> and the thinking in <c>thoughtsTokenCount</c> (two counts that do not overlap) and bills
/// both at the output price. OpenAI and Anthropic report the thinking inside the output count, the price resolver prices
/// the output count and nothing else, and the run screen takes the reasoning count to be at most the output count. The
/// Google provider adds the thinking to the output count at its own boundary, and the price resolver is not told about it:
/// pricing the reasoning count as well would bill the other two providers twice.
/// </para>
/// <para>
/// One scripted call reports 40 input tokens, 60 answer tokens and 150 thinking tokens. At 1000 per million for input and
/// 2000 for output the call costs <c>0.04 + 0.42 = 0.46</c>; priced from the answer alone it would cost <c>0.04 + 0.12</c>.
/// </para>
/// </remarks>
public sealed class GoogleThinkingUsageTests
{
    private const string Usage =
        """{"promptTokenCount":40,"candidatesTokenCount":60,"thoughtsTokenCount":150,"totalTokenCount":250}""";

    [Fact]
    public async Task A_buffered_call_records_the_thinking_inside_the_output_and_prices_it_at_the_output_rate()
    {
        await using var gemini = await FakeGeminiServer.StartAsync();
        gemini.Usage = Usage;

        await using var host = await StartAsync(gemini);

        using var response = await PostBufferedAsync(host);
        response.EnsureSuccessStatusCode();

        AssertThinking(await SingleRunAsync(host));
    }

    [Fact]
    public async Task A_streamed_call_records_the_thinking_inside_the_output_and_prices_it_at_the_output_rate()
    {
        await using var gemini = await FakeGeminiServer.StartAsync();
        gemini.Usage = Usage;

        await using var host = await StartAsync(gemini);

        await PostStreamingAsync(host);

        AssertThinking(await SingleRunAsync(host));
    }

    [Fact]
    public async Task A_call_without_thinking_records_its_output_as_reported_and_no_reasoning_counter()
    {
        await using var gemini = await FakeGeminiServer.StartAsync();
        gemini.Usage = """{"promptTokenCount":40,"candidatesTokenCount":60,"totalTokenCount":100}""";

        await using var host = await StartAsync(gemini);

        using var response = await PostBufferedAsync(host);
        response.EnsureSuccessStatusCode();

        var usage = (await SingleRunAsync(host)).Usage.ShouldNotBeNull();

        usage.OutputTokens.ShouldBe(60);
        usage.ReasoningTokens.ShouldBeNull("a call that reports no thinking tokens has no reasoning counter, and zero would say it measured none");
    }

    [Fact]
    public async Task The_counts_reach_the_wire_of_the_run_endpoint_with_the_reasoning_inside_the_output()
    {
        await using var gemini = await FakeGeminiServer.StartAsync();
        gemini.Usage = Usage;

        await using var host = await StartAsync(gemini);

        using var response = await PostBufferedAsync(host);
        response.EnsureSuccessStatusCode();

        var run = await SingleRunAsync(host);

        using var wire = JsonDocument.Parse(await host.Client.GetStringAsync(
            new Uri($"/tracon/api/runs/{run.Id}", UriKind.Relative),
            TestContext.Current.CancellationToken));

        var usage = wire.RootElement.GetProperty("usage");

        usage.GetProperty("reasoningTokens").GetInt64().ShouldBe(150);
        usage.GetProperty("outputTokens").GetInt64().ShouldBe(210);
        usage.GetProperty("reasoningTokens").GetInt64().ShouldBeLessThanOrEqualTo(
            usage.GetProperty("outputTokens").GetInt64(),
            "the run screen takes the thinking to be a part of the output");
    }

    private static void AssertThinking(RunRecord run)
    {
        var usage = run.Usage.ShouldNotBeNull();

        usage.InputTokens.ShouldBe(40);
        usage.OutputTokens.ShouldBe(210, "the answer (60) and the thinking (150) are both billed as output");
        usage.ReasoningTokens.ShouldBe(150, "the reasoning counter stays what the service reported");

        // The output cost is the output rate times the 210 tokens. A cost of 0.16 would mean the thinking was left out.
        var cost = run.Cost.ShouldNotBeNull();

        cost.InputCost.ShouldBe(0.04m);
        cost.OutputCost.ShouldBe(0.42m);
        cost.Total().ShouldBe(0.46m);
    }

    private static Task<TraconTestHost> StartAsync(FakeGeminiServer gemini)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .UseGoogle("test-gemini-thinking", options =>
                {
                    options.Endpoint = gemini.BaseAddress;
                    options.Models.Add(new ModelDescriptor
                    {
                        Name = "gemini-test",
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
                        Provider = GoogleProviderNames.Google,
                        Model = "gemini-test",
                        MaxOutputTokens = 1_000,
                    },
                    Origin = AgentDefinitionOrigin.Code,
                }),
            configureServices: services => services.Configure<TraconOptions>(options => options.Pricing.Currency = "USD"));
}
