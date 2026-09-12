using System.Diagnostics.Metrics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Diagnostics;

/// <summary>
/// Verifies the Phase 35 contract of the <c>tracon.run.cost</c> counter: it is
/// emitted when a price is defined, the tag set is stable, and it does NOT include
/// the subtree total (K-151).
/// </summary>
/// <remarks>
/// The tests run END TO END through <see cref="RunRecordingAgent"/>; they do not
/// call <see cref="TraconMetrics.RecordCost"/> in isolation — the lesson from
/// Phase 20 (K-157): adding a parameter is not the same as USING it, and if the
/// layer in between is skipped, unit tests will not catch it.
/// </remarks>
public sealed class RunCostMetricTests
{
    [Fact]
    public async Task Cost_is_emitted_and_tags_are_stable_when_price_is_defined()
    {
        var store = new InMemoryRunStore();
        using var meterFactory = new TestMeterFactory();
        using var metrics = new TraconMetrics(meterFactory);
        using var collector = new MetricCollector(meterFactory.Meter);

        var usage = new UsageDetails { InputTokenCount = 1_000_000, OutputTokenCount = 500_000, TotalTokenCount = 1_500_000 };
        var client = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")) { Usage = usage });

        var provider = new FakeModelProvider(client, name: "fake", models:
        [
            new ModelDescriptor { Name = "priced-model", InputCostPerMillionTokens = 2m, OutputCostPerMillionTokens = 4m },
        ]);

        var resolver = new RunPricingResolver(
            new ModelProviderRegistry([provider]),
            Options.Create(new TraconOptions { Pricing = new TraconPricingOptions { Currency = "USD" } }));

        var compiler = new AgentDefinitionCompiler(TestData.Providers(provider), TestData.Registry());
        var definition = TestData.Definition() with { Model = new ModelBinding { Provider = "fake", Model = "priced-model" } };

        var agent = new RunRecordingAgent(
            compiler.Compile(definition),
            store,
            new FixedTenantContext(),
            new TraconRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            metrics: metrics,
            modelId: "priced-model",
            modelProvider: "fake",
            pricingResolver: resolver);

        await agent.RunAsync("hello");

        // $2/M input * 1,000,000 + $4/M output * 500,000 = 2 + 2 = 4.
        collector.DoubleValues(TraconDiagnostics.RunCostCounterName).ShouldBe([4.0]);

        var tags = collector.Tags(TraconDiagnostics.RunCostCounterName).ShouldHaveSingleItem();
        tags.Count.ShouldBe(4);
        tags[TraconDiagnostics.Tags.AgentName].ShouldBe("test-agent");
        tags[TraconDiagnostics.Tags.ModelId].ShouldBe("priced-model");
        tags[TraconDiagnostics.Tags.TenantId].ShouldBe("test");
        tags[TraconDiagnostics.Tags.Currency].ShouldBe("USD");

        // 🚨 run.id is NEVER a tag: every run would open a new time series.
        tags.ShouldNotContainKey(TraconDiagnostics.Tags.RunId);
    }

    [Fact]
    public async Task No_cost_is_emitted_when_price_is_undefined()
    {
        var store = new InMemoryRunStore();
        using var meterFactory = new TestMeterFactory();
        using var metrics = new TraconMetrics(meterFactory);
        using var collector = new MetricCollector(meterFactory.Meter);

        var usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 10, TotalTokenCount = 20 };
        var client = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")) { Usage = usage });

        var provider = new FakeModelProvider(client, name: "fake", models: [new ModelDescriptor { Name = "unpriced-model" }]);
        var resolver = new RunPricingResolver(new ModelProviderRegistry([provider]), Options.Create(new TraconOptions()));
        var compiler = new AgentDefinitionCompiler(TestData.Providers(provider), TestData.Registry());
        var definition = TestData.Definition() with { Model = new ModelBinding { Provider = "fake", Model = "unpriced-model" } };

        var agent = new RunRecordingAgent(
            compiler.Compile(definition),
            store,
            new FixedTenantContext(),
            new TraconRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            metrics: metrics,
            modelId: "unpriced-model",
            modelProvider: "fake",
            pricingResolver: resolver);

        await agent.RunAsync("hello");

        // Emitting an unknown price as zero would understate the real spend;
        // producing no measurement at all is the correct behavior.
        collector.DoubleValues(TraconDiagnostics.RunCostCounterName).ShouldBeEmpty();
    }

    /// <summary>
    /// 🚨 K-151 regression protection: for a root + two sub-runs, the counter
    /// produces only THREE independent measurements, and their sum gives the
    /// three runs' OWN costs, NOT the subtree total.
    /// </summary>
    [Fact]
    public async Task Root_and_two_sub_runs_counter_does_not_double_count_subtree_total()
    {
        var store = new InMemoryRunStore();
        using var meterFactory = new TestMeterFactory();
        using var metrics = new TraconMetrics(meterFactory);
        using var collector = new MetricCollector(meterFactory.Meter);

        // Tokens decreasing by call order: root spends the most, sub-runs spend
        // less. Price is $2/M input, no output.
        var usagesByCall = new Queue<UsageDetails>(
        [
            new UsageDetails { InputTokenCount = 1_000_000, OutputTokenCount = 0, TotalTokenCount = 1_000_000 },
            new UsageDetails { InputTokenCount = 500_000, OutputTokenCount = 0, TotalTokenCount = 500_000 },
            new UsageDetails { InputTokenCount = 250_000, OutputTokenCount = 0, TotalTokenCount = 250_000 },
        ]);

        var client = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")) { Usage = usagesByCall.Dequeue() });

        var provider = new FakeModelProvider(client, name: "fake", models:
        [
            new ModelDescriptor { Name = "priced-model", InputCostPerMillionTokens = 2m },
        ]);

        var resolver = new RunPricingResolver(new ModelProviderRegistry([provider]), Options.Create(new TraconOptions()));
        var compiler = new AgentDefinitionCompiler(TestData.Providers(provider), TestData.Registry());
        var definition = TestData.Definition() with { Model = new ModelBinding { Provider = "fake", Model = "priced-model" } };

        var agent = new RunRecordingAgent(
            compiler.Compile(definition),
            store,
            new FixedTenantContext(),
            new TraconRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            metrics: metrics,
            modelId: "priced-model",
            modelProvider: "fake",
            pricingResolver: resolver);

        var rootRunId = TraconId.NewId();

        // Root: $2/M * 1,000,000 = 2.0
        await agent.RunAsync("root request", options: new TraconRunOptions { RunId = rootRunId });

        // Sub 1: $2/M * 500,000 = 1.0
        await agent.RunAsync(
            "sub request 1",
            options: new TraconRunOptions { ParentRunId = rootRunId, RootRunId = rootRunId, Depth = 1 });

        // Sub 2: $2/M * 250,000 = 0.5
        await agent.RunAsync(
            "sub request 2",
            options: new TraconRunOptions { ParentRunId = rootRunId, RootRunId = rootRunId, Depth = 1 });

        var costs = collector.DoubleValues(TraconDiagnostics.RunCostCounterName);

        costs.Count.ShouldBe(3);
        costs.ShouldBe([2.0, 1.0, 0.5], ignoreOrder: true);

        // The sum is the three runs' OWN cost; it must NOT be the subtree total
        // (root + subs' subtree total counted again).
        costs.Sum().ShouldBe(3.5);
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public string TenantId => "test";
    }

    /// <summary>
    /// Produces a <see cref="Meter"/> with a unique identity, private to the test's
    /// own <see cref="TraconMetrics"/> instance.
    /// </summary>
    /// <remarks>
    /// 🚨 <see cref="MeterListener"/> runs process-wide: filtering by name also catches
    /// measurements from a DIFFERENT <see cref="Meter"/> instance with the SAME name in
    /// another test class running in parallel (see <c>ObservabilityTests.TestMeterFactory</c>).
    /// </remarks>
    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Meter { get; } = new(TraconDiagnostics.MeterName);

        public Meter Create(MeterOptions options) => Meter;

        public void Dispose() => Meter.Dispose();
    }

    /// <summary>
    /// Simple listener that collects measurements of a specific <c>Meter</c>
    /// <strong>instance</strong>. Filtering is by reference, not by name.
    /// </summary>
    private sealed class MetricCollector : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Name, double Value, Dictionary<string, object?> Tags)> _doubles = [];
        private readonly Lock _gate = new();

        public MetricCollector(Meter meter)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter, meter))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            {
                lock (_gate)
                {
                    _doubles.Add((instrument.Name, value, ToDictionary(tags)));
                }
            });

            _listener.Start();
        }

        public List<double> DoubleValues(string name)
        {
            lock (_gate)
            {
                return [.. _doubles.Where(m => string.Equals(m.Name, name, StringComparison.Ordinal)).Select(m => m.Value)];
            }
        }

        public List<Dictionary<string, object?>> Tags(string name)
        {
            lock (_gate)
            {
                return [.. _doubles.Where(m => string.Equals(m.Name, name, StringComparison.Ordinal)).Select(m => m.Tags)];
            }
        }

        public void Dispose() => _listener.Dispose();

        private static Dictionary<string, object?> ToDictionary(ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var tag in tags)
            {
                result[tag.Key] = tag.Value;
            }

            return result;
        }
    }
}
