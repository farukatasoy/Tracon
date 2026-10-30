using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Configuration;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Diagnostics;

/// <summary>
/// <c>tracon.tenant.id</c> is on a metric only when
/// <see cref="TraconObservabilityOptions.IncludeTenantTag"/> is on.
/// </summary>
/// <remarks>
/// <para>
/// The behavior crosses configuration, container and meter, so the first test group runs the way a host does: a
/// configuration key goes through <c>AddTracon</c>, the container builds <see cref="TraconMetrics"/>, and a
/// listener on that metric's own <see cref="Meter"/> instance reads what the eight recording methods publish. A
/// <c>TraconMetrics</c> built by hand would not prove that the container hands it the options.
/// </para>
/// <para>
/// The rest is the other half of the decision: a span carries the tenant whatever the setting says, and a run
/// through <see cref="RunRecordingAgent"/> (whose call sites did not change) reaches the same result. The quota
/// gauges keep the tenant tag too; <c>QuotaUsageObserverTests</c> reads it with the setting off.
/// </para>
/// </remarks>
public sealed class TenantTagOptionTests
{
    private const string Tenant = "tenant-238";

    private static readonly string[] TenantCarryingInstruments =
    [
        TraconDiagnostics.RunCounterName,
        TraconDiagnostics.RunCostCounterName,
        TraconDiagnostics.JudgeCostCounterName,
        TraconDiagnostics.JudgeScoreHistogramName,
        TraconDiagnostics.ModelCacheLookupCounterName,
        TraconDiagnostics.AuditWriteFailureCounterName,
        TraconDiagnostics.RunRecordingFailureCounterName,
        TraconDiagnostics.JobCounterName,
    ];

    [Theory]
    [InlineData(null, false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    public void The_container_hands_the_setting_to_all_eight_metrics(string? configured, bool expectTag)
    {
        using var meterFactory = new TestMeterFactory();
        using var collector = new Measurements(meterFactory.Meter);
        using var provider = BuildProvider(meterFactory, configured);

        RecordEverything(provider.GetRequiredService<TraconMetrics>());

        foreach (var instrument in TenantCarryingInstruments)
        {
            var published = collector.Tags(instrument).ShouldHaveSingleItem($"'{instrument}' published no measurement");

            // A series that lost every tag would pass the assertion below for the wrong reason.
            published.Count.ShouldBeGreaterThan(0);

            if (expectTag)
            {
                published[TraconDiagnostics.Tags.TenantId].ShouldBe(Tenant, $"'{instrument}' lost the tenant tag");
            }
            else
            {
                published.ShouldNotContainKey(TraconDiagnostics.Tags.TenantId, $"'{instrument}' carries the tenant tag by default");
            }
        }
    }

    [Fact]
    public void The_instruments_that_never_carried_a_tenant_still_do_not()
    {
        using var meterFactory = new TestMeterFactory();
        using var collector = new Measurements(meterFactory.Meter);
        using var provider = BuildProvider(meterFactory, "true");
        var metrics = provider.GetRequiredService<TraconMetrics>();

        metrics.RecordRun("agent-a", RunStatus.Completed, Tenant, "model-a", TimeSpan.FromSeconds(1), new RunUsage { InputTokens = 3, OutputTokens = 2 });
        metrics.RecordToolInvocation("tool-a", succeeded: true, TimeSpan.FromSeconds(1));

        // `tracon.runs` has the tag on; its duration histogram, the token counter and the tool instruments never had it.
        collector.Tags(TraconDiagnostics.RunCounterName).ShouldHaveSingleItem().ShouldContainKey(TraconDiagnostics.Tags.TenantId);

        foreach (var instrument in new[]
        {
            TraconDiagnostics.RunDurationName,
            TraconDiagnostics.TokenCounterName,
            TraconDiagnostics.ToolCounterName,
            TraconDiagnostics.ToolDurationName,
        })
        {
            collector.Tags(instrument).ShouldNotBeEmpty();
            collector.Tags(instrument).ShouldAllBe(tags => !tags.ContainsKey(TraconDiagnostics.Tags.TenantId));
        }
    }

    [Fact]
    public void A_change_of_the_setting_reaches_the_next_measurement()
    {
        // The monitor hands out one object, so changing the object is what a reload does to CurrentValue.
        var options = new TraconOptions();

        using var meterFactory = new TestMeterFactory();
        using var collector = new Measurements(meterFactory.Meter);
        using var metrics = new TraconMetrics(meterFactory, new StaticOptionsMonitor<TraconOptions>(options));

        metrics.RecordCost("agent-a", "model-a", Tenant, 1m, "USD");

        options.Observability.IncludeTenantTag = true;
        metrics.RecordCost("agent-a", "model-a", Tenant, 1m, "USD");

        var published = collector.Tags(TraconDiagnostics.RunCostCounterName);

        published.Count.ShouldBe(2);
        published[0].ShouldNotContainKey(TraconDiagnostics.Tags.TenantId);
        published[1][TraconDiagnostics.Tags.TenantId].ShouldBe(Tenant);
    }

    [Fact]
    public async Task A_span_carries_the_tenant_whatever_the_setting_and_the_metrics_follow_the_setting()
    {
        foreach (var includeTenantTag in new[] { false, true })
        {
            var tenant = $"{Tenant}-span-{includeTenantTag}";
            using var meterFactory = new TestMeterFactory();
            using var collector = new Measurements(meterFactory.Meter);
            using var spans = new SpanCapture(tenant);

            var options = new TraconOptions();
            options.Observability.IncludeTenantTag = includeTenantTag;

            using var metrics = new TraconMetrics(meterFactory, new StaticOptionsMonitor<TraconOptions>(options));

            await RunAgentAsync(metrics, tenant);

            var span = spans.Runs.ShouldHaveSingleItem();
            span.GetTagItem(TraconDiagnostics.Tags.TenantId).ShouldBe(tenant);

            // The agent was not told about the setting: it passes the tenant to the metrics as it always did.
            var runs = collector.Tags(TraconDiagnostics.RunCounterName).ShouldHaveSingleItem();
            var cost = collector.Tags(TraconDiagnostics.RunCostCounterName).ShouldHaveSingleItem();

            runs.ContainsKey(TraconDiagnostics.Tags.TenantId).ShouldBe(includeTenantTag);
            cost.ContainsKey(TraconDiagnostics.Tags.TenantId).ShouldBe(includeTenantTag);

            if (includeTenantTag)
            {
                runs[TraconDiagnostics.Tags.TenantId].ShouldBe(tenant);
                cost[TraconDiagnostics.Tags.TenantId].ShouldBe(tenant);
            }
        }
    }

    [Theory]
    [InlineData("evet")]
    [InlineData("1")]
    [InlineData("on")]
    public void A_value_that_is_not_true_or_false_stops_the_application_at_startup(string configured)
    {
        var failure = SectionBinding.FailsAtStartup(
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["Tracon:Observability:IncludeTenantTag"] = configured });

        string.Join(" ", failure.Failures).ShouldContain($"IncludeTenantTag is '{configured}'");
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("")]
    public void A_readable_or_empty_value_starts_cleanly(string configured)
        => SectionBinding.StartsCleanly(
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["Tracon:Observability:IncludeTenantTag"] = configured });

    [Fact]
    public void Code_that_sets_the_option_after_binding_wins_over_an_unreadable_value()
    {
        SectionBinding.StartsCleanly(
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["Tracon:Observability:IncludeTenantTag"] = "evet" },
            static (builder, _) => builder.Services.Configure<TraconOptions>(static options => options.Observability.IncludeTenantTag = true));
    }

    private static ServiceProvider BuildProvider(IMeterFactory meterFactory, string? configured)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);

        if (configured is not null)
        {
            values["Tracon:Observability:IncludeTenantTag"] = configured;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSingleton(meterFactory);
        services.AddTracon(configuration.GetSection(TraconOptions.SectionName));

        return services.BuildServiceProvider();
    }

    private static void RecordEverything(TraconMetrics metrics)
    {
        metrics.RecordRun("agent-a", RunStatus.Completed, Tenant, "model-a", TimeSpan.FromSeconds(1), usage: null);
        metrics.RecordCost("agent-a", "model-a", Tenant, 1m, "USD");
        metrics.RecordJudgeCost("judge-a", "model-a", Tenant, 1m, "USD");
        metrics.RecordJudgeScore("judge-a", "agent-a", Tenant, 80);
        metrics.RecordModelCacheLookup("provider-a", Tenant, hit: true);
        metrics.RecordAuditWriteFailure(Tenant, "approval.decision", "swallowed");
        metrics.RecordRunRecordingFailure(Tenant, "start");
        metrics.RecordJob("default", "handler-a", JobStatus.Completed, Tenant, TimeSpan.FromSeconds(1));
    }

    private static async Task RunAgentAsync(TraconMetrics metrics, string tenant)
    {
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
            new InMemoryRunStore(),
            new FixedTenantContext(tenant),
            new TraconRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            metrics: metrics,
            modelId: "priced-model",
            modelProvider: "fake",
            pricingResolver: resolver);

        await agent.RunAsync("hello");
    }

    private sealed class FixedTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId => tenantId;
    }

    /// <summary>Collects the tags of every measurement published on one <see cref="Meter"/> instance, by reference.</summary>
    private sealed class Measurements : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Name, Dictionary<string, object?> Tags)> _measurements = [];

        public Measurements(Meter meter)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter, meter))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => Add(instrument.Name, tags));
            _listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) => Add(instrument.Name, tags));
            _listener.Start();
        }

        public List<Dictionary<string, object?>> Tags(string name)
        {
            lock (_measurements)
            {
                return [.. _measurements.Where(m => string.Equals(m.Name, name, StringComparison.Ordinal)).Select(m => m.Tags)];
            }
        }

        public void Dispose() => _listener.Dispose();

        private void Add(string name, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var copied = new Dictionary<string, object?>(tags.Length, StringComparer.Ordinal);

            foreach (var tag in tags)
            {
                copied[tag.Key] = tag.Value;
            }

            lock (_measurements)
            {
                _measurements.Add((name, copied));
            }
        }
    }

    /// <summary>
    /// Captures the run spans of ONE tenant. <see cref="ActivityListener"/> is process-wide and other test
    /// classes run at the same time, so the capture keeps only the spans that carry this test's tenant.
    /// </summary>
    private sealed class SpanCapture : IDisposable
    {
        private readonly ActivityListener _listener;
        private readonly List<Activity> _runs = [];

        public SpanCapture(string tenant)
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = static source => string.Equals(source.Name, TraconDiagnostics.ActivitySourceName, StringComparison.Ordinal),
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity =>
                {
                    if (string.Equals(activity.OperationName, TraconDiagnostics.RunActivityName, StringComparison.Ordinal) &&
                        string.Equals(activity.GetTagItem(TraconDiagnostics.Tags.TenantId) as string, tenant, StringComparison.Ordinal))
                    {
                        lock (_runs)
                        {
                            _runs.Add(activity);
                        }
                    }
                },
            };

            ActivitySource.AddActivityListener(_listener);
        }

        public List<Activity> Runs
        {
            get
            {
                lock (_runs)
                {
                    return [.. _runs];
                }
            }
        }

        public void Dispose() => _listener.Dispose();
    }
}
