using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// Verifies how <c>UsageDetails</c> maps onto <see cref="RunUsage"/>.
/// </summary>
/// <remarks>
/// The load-bearing assertion in this file is the NEGATIVE one: a provider that
/// reports no cache count must leave the field <see langword="null"/>. Writing
/// zero would report an unobserved 0% cache hit rate as a measurement, and every
/// cost report downstream would inherit that claim.
/// </remarks>
public sealed class RunUsageMappingTests
{
    [Fact]
    public async Task A_provider_that_reports_no_breakdown_leaves_every_breakdown_field_null()
    {
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var agent = CreateAgent(store, UsageOf(input: 100, output: 50, configure: null));

        await agent.RunAsync("hello");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        run.Usage.ShouldNotBeNull();
        run.Usage.InputTokens.ShouldBe(100);
        run.Usage.OutputTokens.ShouldBe(50);

        run.Usage.CachedInputTokens.ShouldBeNull();
        run.Usage.ReasoningTokens.ShouldBeNull();
        run.Usage.AudioInputTokens.ShouldBeNull();
        run.Usage.AudioOutputTokens.ShouldBeNull();
    }

    [Fact]
    public async Task A_reported_zero_is_kept_as_zero_and_is_not_collapsed_into_null()
    {
        // "Measured, and it was none" is a different statement from "not
        // measured". A provider that explicitly reports 0 cache hits made an
        // observation, and it must survive the round trip.
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var agent = CreateAgent(store, UsageOf(100, 50, usage => usage.CachedInputTokenCount = 0));

        await agent.RunAsync("hello");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        run.Usage.ShouldNotBeNull();
        run.Usage.CachedInputTokens.ShouldBe(0);
    }

    [Fact]
    public async Task Every_reported_breakdown_counter_reaches_the_record()
    {
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var agent = CreateAgent(store, UsageOf(100, 50, usage =>
        {
            usage.CachedInputTokenCount = 40;
            usage.ReasoningTokenCount = 30;
#pragma warning disable MEAI001 // Evaluation-only members; production reads are consolidated in UsageBreakdown.
            usage.InputAudioTokenCount = 20;
            usage.OutputAudioTokenCount = 10;
#pragma warning restore MEAI001
        }));

        await agent.RunAsync("hello");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        run.Usage.ShouldNotBeNull();
        run.Usage.CachedInputTokens.ShouldBe(40);
        run.Usage.ReasoningTokens.ShouldBe(30);
        run.Usage.AudioInputTokens.ShouldBe(20);
        run.Usage.AudioOutputTokens.ShouldBe(10);
    }

    [Fact]
    public async Task The_breakdown_stays_inside_the_totals_and_is_never_added_to_them()
    {
        // 🚨 The Microsoft.Extensions.AI contract: cached input tokens are part of
        // the input token count. If the mapping ever starts adding them, this
        // assertion is what catches it.
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var agent = CreateAgent(store, UsageOf(100, 50, usage =>
        {
            usage.CachedInputTokenCount = 40;
            usage.ReasoningTokenCount = 30;
        }));

        await agent.RunAsync("hello");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        run.Usage.ShouldNotBeNull();
        run.Usage.InputTokens.ShouldBe(100);
        run.Usage.OutputTokens.ShouldBe(50);
        run.Usage.CachedInputTokens!.Value.ShouldBeLessThanOrEqualTo(run.Usage.InputTokens!.Value);
        run.Usage.ReasoningTokens!.Value.ShouldBeLessThanOrEqualTo(run.Usage.OutputTokens!.Value);
    }

    [Fact]
    public async Task Cache_write_counters_in_AdditionalCounts_reach_the_record_inside_the_input_total()
    {
        // The write counters are not UsageDetails properties: a provider reports them under the
        // TraconUsageCounts keys. They are counted INSIDE InputTokens, never added to it.
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var agent = CreateAgent(store, UsageOf(100, 50, usage => usage.AdditionalCounts = new AdditionalPropertiesDictionary<long>
        {
            [TraconUsageCounts.CacheWriteInputTokens] = 60,
            [TraconUsageCounts.CacheWriteLongInputTokens] = 30,
        }));

        await agent.RunAsync("hello");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        run.Usage.ShouldNotBeNull();
        run.Usage.CacheWriteInputTokens.ShouldBe(60);
        run.Usage.CacheWriteLongInputTokens.ShouldBe(30);
        run.Usage.InputTokens.ShouldBe(100);
    }

    [Fact]
    public async Task A_provider_that_reports_no_cache_write_leaves_both_write_counters_null()
    {
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var agent = CreateAgent(store, UsageOf(100, 50, usage => usage.AdditionalCounts = new AdditionalPropertiesDictionary<long>
        {
            ["SomeOtherCount"] = 9,
        }));

        await agent.RunAsync("hello");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        run.Usage.ShouldNotBeNull();
        run.Usage.CacheWriteInputTokens.ShouldBeNull();
        run.Usage.CacheWriteLongInputTokens.ShouldBeNull();
    }

    /// <summary>
    /// The class guard of the one mapping: every property of <see cref="RunUsage"/> is
    /// filled from a <see cref="UsageDetails"/> that reports everything. A counter added to
    /// <see cref="RunUsage"/> and not to <see cref="UsageBreakdown.ToRunUsage"/> would stay null
    /// at the run recorder, the budget ring and both judges at once.
    /// </summary>
    [Fact]
    public void Every_property_of_RunUsage_is_filled_by_the_one_mapping()
    {
        var details = new UsageDetails
        {
            InputTokenCount = 1_000,
            OutputTokenCount = 500,
            TotalTokenCount = 1_500,
            CachedInputTokenCount = 400,
            ReasoningTokenCount = 200,
            AdditionalCounts = new AdditionalPropertiesDictionary<long>
            {
                [TraconUsageCounts.CacheWriteInputTokens] = 300,
                [TraconUsageCounts.CacheWriteLongInputTokens] = 100,
            },
        };
#pragma warning disable MEAI001 // Evaluation-only members; production reads are consolidated in UsageBreakdown.
        details.InputAudioTokenCount = 50;
        details.OutputAudioTokenCount = 25;
#pragma warning restore MEAI001

        var mapped = UsageBreakdown.ToRunUsage(details);

        foreach (var property in typeof(RunUsage).GetProperties())
        {
            property.GetValue(mapped).ShouldNotBeNull($"UsageBreakdown.ToRunUsage does not fill {property.Name}");
        }

        mapped.CacheWriteInputTokens.ShouldBe(300);
        mapped.CacheWriteLongInputTokens.ShouldBe(100);
    }

    private static RunRecordingAgent CreateAgent(IRunStore store, UsageDetails usage)
    {
        var client = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
        {
            Usage = usage,
        });

        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider(client)),
            TestData.Registry());

        return new RunRecordingAgent(
            compiler.Compile(TestData.Definition()),
            store,
            FixedTenantContext.Default,
            new TraconRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance);
    }

    private static UsageDetails UsageOf(long input, long output, Action<UsageDetails>? configure)
    {
        var usage = new UsageDetails
        {
            InputTokenCount = input,
            OutputTokenCount = output,
            TotalTokenCount = input + output,
        };

        configure?.Invoke(usage);

        return usage;
    }
}
