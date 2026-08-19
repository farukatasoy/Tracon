using System.Diagnostics.Metrics;
using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Diagnostics;

/// <summary>
/// Pins the metric tag sets so run attribution can never leak into them.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 This is a CARDINALITY guard, not a formatting test. A user identity and a
/// free-form label set are unbounded key spaces; promoting either to a metric
/// dimension multiplies the time series of <c>agentprism.tokens</c> and
/// <c>agentprism.run.cost</c> without limit, and the damage lands in the
/// consumer's metrics backend rather than in AgentPrism. Attribution is a QUERY
/// dimension: it lives in the <c>runs</c> table and nowhere else.
/// </para>
/// <para>
/// The tests run END TO END through <see cref="RunRecordingAgent"/> with an
/// attribution context that DOES return a user and labels — asserting on
/// <see cref="AgentPrismMetrics"/> in isolation would pass even if the recording
/// path started adding tags of its own.
/// </para>
/// </remarks>
public sealed class TelemetryTagTests
{
    [Fact]
    public async Task Attribution_never_becomes_a_metric_tag()
    {
        using var meterFactory = new TestMeterFactory();
        using var metrics = new AgentPrismMetrics(meterFactory);
        using var collector = new TagCollector(meterFactory.Meter);

        await RunOnceAsync(metrics);

        collector.TagKeys.ShouldNotBeEmpty();

        foreach (var (instrument, keys) in collector.TagKeys)
        {
            keys.ShouldNotContain(key => string.Equals(key, "agentprism.user.id", StringComparison.Ordinal), $"{instrument} must not carry a user tag.");
            keys.ShouldNotContain(key => string.Equals(key, "agentprism.run.labels", StringComparison.Ordinal), $"{instrument} must not carry a label tag.");

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
        using var metrics = new AgentPrismMetrics(meterFactory);
        using var collector = new TagCollector(meterFactory.Meter);

        await RunOnceAsync(metrics);

        // The full expected set, written out rather than derived: a derived
        // expectation grows silently along with the code it is meant to pin.
        collector.KeysOf(AgentPrismDiagnostics.RunCounterName).ShouldBe(
            [
                AgentPrismDiagnostics.Tags.AgentName,
                AgentPrismDiagnostics.Tags.Status,
                AgentPrismDiagnostics.Tags.TenantId,
            ],
            ignoreOrder: true);

        collector.KeysOf(AgentPrismDiagnostics.RunDurationName).ShouldBe(
            [
                AgentPrismDiagnostics.Tags.AgentName,
                AgentPrismDiagnostics.Tags.Status,
            ],
            ignoreOrder: true);

        collector.KeysOf(AgentPrismDiagnostics.TokenCounterName).ShouldBe(
            [
                AgentPrismDiagnostics.Tags.AgentName,
                AgentPrismDiagnostics.Tags.ModelId,
                AgentPrismDiagnostics.Tags.Direction,
            ],
            ignoreOrder: true);

        collector.KeysOf(AgentPrismDiagnostics.RunCostCounterName).ShouldBe(
            [
                AgentPrismDiagnostics.Tags.AgentName,
                AgentPrismDiagnostics.Tags.ModelId,
                AgentPrismDiagnostics.Tags.TenantId,
                AgentPrismDiagnostics.Tags.Currency,
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
        using var metrics = new AgentPrismMetrics(meterFactory);
        using var collector = new TagCollector(meterFactory.Meter);

        await RunOnceAsync(metrics);

        collector.ValuesOf(AgentPrismDiagnostics.TokenCounterName, AgentPrismDiagnostics.Tags.Direction)
            .ShouldBe(["input", "output"], ignoreOrder: true);
    }

    private static async Task RunOnceAsync(AgentPrismMetrics metrics)
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
            Options.Create(new AgentPrismOptions { Pricing = new AgentPrismPricingOptions { Currency = "USD" } }));

        var compiler = new AgentDefinitionCompiler(TestData.Providers(provider), TestData.Registry());
        var definition = TestData.Definition() with { Model = new ModelBinding { Provider = "fake", Model = "priced-model" } };

        var agent = new RunRecordingAgent(
            compiler.Compile(definition),
            store,
            FixedTenantContext.Default,
            new AgentPrismRunRecordingOptions(),
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
        public Meter Meter { get; } = new(AgentPrismDiagnostics.MeterName);

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

    private sealed class TagCollector : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Instrument, Dictionary<string, object?> Tags)> _measurements = [];
        private readonly Lock _gate = new();

        public TagCollector(Meter meter)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter, meter))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => Record(instrument.Name, tags));
            _listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) => Record(instrument.Name, tags));
            _listener.Start();
        }

        public IReadOnlyList<(string Instrument, IReadOnlyCollection<string> Keys)> TagKeys
        {
            get
            {
                lock (_gate)
                {
                    return [.. _measurements.Select(static m => (m.Instrument, (IReadOnlyCollection<string>)m.Tags.Keys))];
                }
            }
        }

        public HashSet<string> KeysOf(string instrument)
        {
            lock (_gate)
            {
                var keys = new HashSet<string>(StringComparer.Ordinal);

                foreach (var measurement in _measurements.Where(m => string.Equals(m.Instrument, instrument, StringComparison.Ordinal)))
                {
                    keys.UnionWith(measurement.Tags.Keys);
                }

                return keys;
            }
        }

        public IReadOnlyCollection<string> ValuesOf(string instrument, string tag)
        {
            lock (_gate)
            {
                return [.. _measurements
                    .Where(m => string.Equals(m.Instrument, instrument, StringComparison.Ordinal) && m.Tags.ContainsKey(tag))
                    .Select(m => m.Tags[tag]?.ToString() ?? string.Empty)
                    .Distinct(StringComparer.Ordinal)];
            }
        }

        public void Dispose() => _listener.Dispose();

        private void Record(string instrument, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var map = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var tag in tags)
            {
                map[tag.Key] = tag.Value;
            }

            lock (_gate)
            {
                _measurements.Add((instrument, map));
            }
        }
    }
}
