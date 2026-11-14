using System.Diagnostics.Metrics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Diagnostics;

/// <summary>
/// Pins the metric tag sets so run attribution can never leak into them.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 This is a CARDINALITY guard, not a formatting test. A user identity and a
/// free-form label set are unbounded key spaces; promoting either to a metric
/// dimension multiplies the time series of <c>tracon.tokens</c> and
/// <c>tracon.run.cost</c> without limit, and the damage lands in the
/// consumer's metrics backend rather than in Tracon. Attribution is a QUERY
/// dimension: it lives in the <c>runs</c> table and nowhere else.
/// </para>
/// <para>
/// The tests run END TO END through <see cref="RunRecordingAgent"/> with an
/// attribution context that DOES return a user and labels — asserting on
/// <see cref="TraconMetrics"/> in isolation would pass even if the recording
/// path started adding tags of its own.
/// </para>
/// </remarks>
public sealed class TelemetryTagTests
{
    [Fact]
    public async Task Attribution_never_becomes_a_metric_tag()
    {
        using var meterFactory = new TestMeterFactory();
        using var metrics = new TraconMetrics(meterFactory, TenantTaggedMetricsOptions.Create());
        using var collector = new MetricCollector(meterFactory.Meter);

        await RunOnceAsync(metrics);

        TagKeys(collector).ShouldNotBeEmpty();

        foreach (var (instrument, keys) in TagKeys(collector))
        {
            keys.ShouldNotContain(key => string.Equals(key, "tracon.user.id", StringComparison.Ordinal), $"{instrument} must not carry a user tag.");
            keys.ShouldNotContain(key => string.Equals(key, "tracon.run.labels", StringComparison.Ordinal), $"{instrument} must not carry a label tag.");

            // The label KEYS themselves must not appear either — the failure mode
            // is a tag named after each label, not one tag holding the whole map.
            keys.ShouldNotContain(key => string.Equals(key, "team", StringComparison.Ordinal), $"{instrument} must not carry a per-label tag.");
            keys.ShouldNotContain(key => string.Equals(key, "ticket", StringComparison.Ordinal), $"{instrument} must not carry a per-label tag.");
        }
    }

    [Fact]
    public async Task The_tag_set_of_every_run_instrument_is_exactly_what_it_was()
    {
        using var meterFactory = new TestMeterFactory();
        using var metrics = new TraconMetrics(meterFactory, TenantTaggedMetricsOptions.Create());
        using var collector = new MetricCollector(meterFactory.Meter);

        await RunOnceAsync(metrics);

        // The full expected set, written out rather than derived: a derived
        // expectation grows silently along with the code it is meant to pin.
        KeysOf(collector, TraconDiagnostics.RunCounterName).ShouldBe(
            [
                TraconDiagnostics.Tags.AgentName,
                TraconDiagnostics.Tags.Status,
                TraconDiagnostics.Tags.TenantId,
            ],
            ignoreOrder: true);

        KeysOf(collector, TraconDiagnostics.RunDurationName).ShouldBe(
            [
                TraconDiagnostics.Tags.AgentName,
                TraconDiagnostics.Tags.Status,
            ],
            ignoreOrder: true);

        KeysOf(collector, TraconDiagnostics.TokenCounterName).ShouldBe(
            [
                TraconDiagnostics.Tags.AgentName,
                TraconDiagnostics.Tags.ModelId,
                TraconDiagnostics.Tags.Direction,
            ],
            ignoreOrder: true);

        KeysOf(collector, TraconDiagnostics.RunCostCounterName).ShouldBe(
            [
                TraconDiagnostics.Tags.AgentName,
                TraconDiagnostics.Tags.ModelId,
                TraconDiagnostics.Tags.TenantId,
                TraconDiagnostics.Tags.Currency,
            ],
            ignoreOrder: true);
    }

    [Fact]
    public async Task The_token_counter_reports_only_input_and_output_directions()
    {
        // 🚨 The breakdown counters are counted INSIDE input/output. Emitting
        // them as extra `direction` values would double count every token in the
        // consumer's dashboard, on top of tripling the series count.
        using var meterFactory = new TestMeterFactory();
        using var metrics = new TraconMetrics(meterFactory, TenantTaggedMetricsOptions.Create());
        using var collector = new MetricCollector(meterFactory.Meter);

        await RunOnceAsync(metrics);

        ValuesOf(collector, TraconDiagnostics.TokenCounterName, TraconDiagnostics.Tags.Direction)
            .ShouldBe(["input", "output"], ignoreOrder: true);
    }

    private static async Task RunOnceAsync(TraconMetrics metrics)
    {
        var store = new InMemoryRunStore();

        var usage = new UsageDetails
        {
            InputTokenCount = 1_000_000,
            OutputTokenCount = 500_000,
            TotalTokenCount = 1_500_000,
            CachedInputTokenCount = 400_000,
            ReasoningTokenCount = 100_000,
        };

        var client = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")) { Usage = usage });

        var provider = new FakeModelProvider(client, name: "fake", models:
        [
            new ModelDescriptor
            {
                Name = "priced-model",
                InputCostPerMillionTokens = 2m,
                OutputCostPerMillionTokens = 4m,
                CachedInputCostPerMillionTokens = 1m,
            },
        ]);

        var resolver = new RunPricingResolver(
            new ModelProviderRegistry([provider]),
            Options.Create(new TraconOptions { Pricing = new TraconPricingOptions { Currency = "USD" } }));

        var compiler = new AgentDefinitionCompiler(TestData.Providers(provider), TestData.Registry());
        var definition = TestData.Definition() with { Model = new ModelBinding { Provider = "fake", Model = "priced-model" } };

        var agent = TestRunRecording.Agent(
            compiler.Compile(definition),
            store,
            FixedTenantContext.Default,
            new TraconRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            metrics: metrics,
            modelId: "priced-model",
            modelProvider: "fake",
            pricingResolver: resolver,
            attributionContext: new StubAttributionContext());

        await agent.RunAsync("hello");

        // The attribution really did land on the row — otherwise the assertions
        // above would pass for the wrong reason.
        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.UserId.ShouldBe("user-42");
        run.Labels.ShouldNotBeNull();
        run.Labels!["team"].ShouldBe("payments");
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Meter { get; } = new(TraconDiagnostics.MeterName);

        public Meter Create(MeterOptions options) => Meter;

        public void Dispose() => Meter.Dispose();
    }

    private sealed class StubAttributionContext : IRunAttributionContext
    {
        public string? UserId => "user-42";

        public IReadOnlyDictionary<string, string>? Labels => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["team"] = "payments",
            ["ticket"] = "OPS-1234",
        };
    }

    /// <summary>The tag keys of every measurement, with the instrument that published it.</summary>
    private static List<(string Instrument, IEnumerable<string> Keys)> TagKeys(MetricCollector collector)
        => [.. collector.Snapshot().Select(static m => (m.Instrument, (IEnumerable<string>)m.Tags.Keys))];

    /// <summary>Every tag key one instrument published, over all its measurements.</summary>
    private static HashSet<string> KeysOf(MetricCollector collector, string instrument)
        => [.. collector.Tags(instrument).SelectMany(static tags => tags.Keys)];

    /// <summary>The distinct values one instrument published for one tag.</summary>
    private static IReadOnlyCollection<string> ValuesOf(MetricCollector collector, string instrument, string tag)
        => [.. collector.Tags(instrument)
            .Where(tags => tags.ContainsKey(tag))
            .Select(tags => tags[tag]?.ToString() ?? string.Empty)
            .Distinct(StringComparer.Ordinal)];
}
