using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// The tokens a side-channel call writes into a provider's prompt cache reach the run's usage and its
/// cost (K-483's class).
/// </summary>
/// <remarks>
/// <para>
/// The compaction summarizer, the <c>aiJudge</c> loop judge, a discarded repair turn and the turns of a
/// loop that fails its run spend tokens the <c>AgentResponse</c> never carries. <see cref="RunRecordingAgent"/>
/// adds them to the run with <see cref="RunUsageSum"/> in three places: the agent's own share, every other
/// model's share, and a share that names the agent's own model (<see cref="RunCostCombiner"/>). A counter
/// <see cref="RunUsageSum"/> does not carry is dropped at all three, and the written tokens are then priced
/// as plain input while every run-level total falls short.
/// </para>
/// <para>
/// Each test feeds the run's ambient side-channel accumulator from inside the agent's one model call, the
/// way the production sources do, and reads the run back from the store.
/// </para>
/// </remarks>
public sealed class SideChannelCacheWriteRunTests
{
    private const string Provider = "fake";
    private const string AgentModel = "priced-model";
    private const string OtherModel = "other-model";

    [Fact]
    public async Task A_side_channel_call_on_the_agents_own_model_adds_its_cache_writes_to_the_run_and_prices_them()
    {
        // The agent's response reports 200 K short writes; the side channel adds 50 K short and 300 K long.
        var run = await RunAsync(side => side.Add(Details(400_000, 40_000, shortWrite: 50_000, longWrite: 300_000)));

        var usage = run.Usage.ShouldNotBeNull();
        usage.InputTokens.ShouldBe(1_400_000);
        usage.OutputTokens.ShouldBe(140_000);
        usage.CacheWriteInputTokens.ShouldBe(250_000);
        usage.CacheWriteLongInputTokens.ShouldBe(300_000);

        // Priced at the agent's model (input 2, output 4, write 2.5, long write 4 per million):
        // 1.4 M input - 250 K - 300 K written = 850 K at the full input price.
        var cost = run.Cost.ShouldNotBeNull();
        cost.Source.ShouldBe(PricingSource.Catalog);
        cost.InputCost.ShouldBe(1.7m);
        cost.OutputCost.ShouldBe(0.56m);
        cost.CacheWriteCost.ShouldBe(0.625m + 1.2m);
        cost.CacheWritePricePerMillionTokens.ShouldBe(2.5m);
        cost.CacheWriteLongPricePerMillionTokens.ShouldBe(4m);
        cost.Total().ShouldBe(1.7m + 0.56m + 1.825m);
    }

    [Fact]
    public async Task A_side_channel_call_on_another_model_adds_its_cache_writes_and_prices_them_at_that_models_rates()
    {
        // The summarizer or the judge runs on a model of its own: input 100, output 300, write 125, long write 200.
        var run = await RunAsync(side => side.Add(
            Details(400_000, 40_000, shortWrite: 100_000, longWrite: 50_000),
            Provider,
            OtherModel));

        // The run's usage is the sum of every share.
        var usage = run.Usage.ShouldNotBeNull();
        usage.InputTokens.ShouldBe(1_400_000);
        usage.CacheWriteInputTokens.ShouldBe(300_000);
        usage.CacheWriteLongInputTokens.ShouldBe(50_000);

        // Each share is priced at the model that spent it.
        // Agent: 800 K plain input x 2, 100 K output x 4, 200 K written x 2.5.
        // Other: 250 K plain input x 100, 40 K output x 300, 100 K written x 125, 50 K long-written x 200.
        var cost = run.Cost.ShouldNotBeNull();
        cost.InputCost.ShouldBe(1.6m + 25m);
        cost.OutputCost.ShouldBe(0.4m + 12m);
        cost.CacheWriteCost.ShouldBe(0.5m + 12.5m + 10m);
        cost.Total().ShouldBe(62m);

        // One rate cannot describe a total that two rates produced.
        cost.CacheWritePricePerMillionTokens.ShouldBeNull();
        cost.CacheWriteLongPricePerMillionTokens.ShouldBeNull();
    }

    [Fact]
    public async Task A_side_channel_share_that_names_the_agents_own_model_is_priced_with_the_agents_share()
    {
        // The same call as the first test, reported the way a summarizer with no model of its own reports.
        var run = await RunAsync(side => side.Add(
            Details(400_000, 40_000, shortWrite: 50_000, longWrite: 300_000),
            Provider,
            AgentModel));

        var usage = run.Usage.ShouldNotBeNull();
        usage.CacheWriteInputTokens.ShouldBe(250_000);
        usage.CacheWriteLongInputTokens.ShouldBe(300_000);

        // The share joins the agent's share before the price is resolved: the answer is the resolver's for the
        // agent's model, rates included, with the written tokens priced apart from the plain input.
        var cost = run.Cost.ShouldNotBeNull();
        cost.InputCost.ShouldBe(1.7m);
        cost.CacheWriteCost.ShouldBe(1.825m);
        cost.CacheWritePricePerMillionTokens.ShouldBe(2.5m);
        cost.CacheWriteLongPricePerMillionTokens.ShouldBe(4m);
    }

    /// <summary>
    /// Runs an agent whose one model call spends <paramref name="sideChannel"/> through the run's accumulator and
    /// answers with 1 M input tokens (200 K of them written with the short time to live) and 100 K output tokens.
    /// </summary>
    private static async Task<RunRecord> RunAsync(Action<SideChannelUsageAccumulator> sideChannel)
    {
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);

        var client = new FakeChatClient(_ =>
        {
            sideChannel(TraconRunContext.Current.ShouldNotBeNull().ExtraUsage.ShouldNotBeNull());

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
            {
                Usage = Details(1_000_000, 100_000, shortWrite: 200_000, longWrite: null),
            };
        });

        var provider = new FakeModelProvider(client, name: Provider, models:
        [
            new ModelDescriptor
            {
                Name = AgentModel,
                InputCostPerMillionTokens = 2m,
                OutputCostPerMillionTokens = 4m,
                CacheWriteCostPerMillionTokens = 2.5m,
                CacheWriteLongCostPerMillionTokens = 4m,
            },
            new ModelDescriptor
            {
                Name = OtherModel,
                InputCostPerMillionTokens = 100m,
                OutputCostPerMillionTokens = 300m,
                CacheWriteCostPerMillionTokens = 125m,
                CacheWriteLongCostPerMillionTokens = 200m,
            },
        ]);

        var compiler = new AgentDefinitionCompiler(TestData.Providers(provider), TestData.Registry());
        var definition = TestData.Definition() with { Model = new ModelBinding { Provider = Provider, Model = AgentModel } };
        var resolver = new RunPricingResolver(new ModelProviderRegistry([provider]), Options.Create(new TraconOptions()));

        var agent = new RunRecordingAgent(
            compiler.Compile(definition),
            store,
            FixedTenantContext.Default,
            new TraconRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            modelId: AgentModel,
            modelProvider: Provider,
            pricingResolver: resolver);

        await agent.RunAsync("hello");

        client.CallCount.ShouldBe(1, "the side-channel usage is fed once, from the one model call");

        return (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
    }

    private static UsageDetails Details(long input, long output, long? shortWrite, long? longWrite)
    {
        var details = new UsageDetails
        {
            InputTokenCount = input,
            OutputTokenCount = output,
            TotalTokenCount = input + output,
        };

        if (shortWrite is not null || longWrite is not null)
        {
            details.AdditionalCounts = new AdditionalPropertiesDictionary<long>();

            if (shortWrite is { } shortCount)
            {
                details.AdditionalCounts[TraconUsageCounts.CacheWriteInputTokens] = shortCount;
            }

            if (longWrite is { } longCount)
            {
                details.AdditionalCounts[TraconUsageCounts.CacheWriteLongInputTokens] = longCount;
            }
        }

        return details;
    }
}
