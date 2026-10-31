using System.Diagnostics.Metrics;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Pins the telemetry names Tracon publishes to <c>docs/sozlesme/telemetri.txt</c>.
/// </summary>
/// <remarks>
/// <para>
/// A dashboard or an alert rule is written against a name, a unit, an instrument kind and the attributes a series
/// carries. Renaming any of them, or flipping an attribute's default, empties the dashboard without a build
/// error. Two kinds of line therefore carry more than a name: an instrument line has its kind, its unit and the
/// attributes it publishes in the default configuration, so that an attribute turned off by default
/// (<c>tracon.tenant.id</c>) is a visible change of the line.
/// </para>
/// <para>
/// Two sources check each other. The constants of <see cref="TraconDiagnostics"/> are the names; the instruments
/// that a container really creates (a <see cref="MeterListener"/> on the meter the container was given) are what is
/// published. An instrument that no constant names is red, and so is a constant for an instrument nobody creates.
/// The attribute tags of a gauge are not listed: a gauge reads the database, and its tags are pinned by
/// <c>QuotaUsageObserverTests</c> and <c>JobQueueDepthGaugeTests</c>.
/// </para>
/// </remarks>
public sealed class TelemetryNameSnapshotTests
{
    private static readonly Dictionary<Type, string> ValueTypeNames = new()
    {
        [typeof(long)] = "long",
        [typeof(double)] = "double",
        [typeof(int)] = "int",
        [typeof(decimal)] = "decimal",
    };

    [Fact]
    public void The_snapshot_matches_the_names_in_the_code()
    {
        ContractSnapshot.Verify(
            "docs/sozlesme/telemetri.txt",
            "tests/Tracon.Core.UnitTests/Architecture/TelemetryNameSnapshotTests.cs",
            "The telemetry Tracon publishes: sources, meters, spans, instruments (kind, unit, default attributes) and attributes",
            Lines());
    }

    [Fact]
    public void Every_instrument_the_container_creates_is_named_by_a_constant_and_the_other_way_round()
    {
        var constants = InstrumentConstants();
        var published = PublishInstruments().Select(static instrument => instrument.Name).ToList();

        // 2026-10-29: 15 counters and histograms of TraconMetrics (an earlier change added the rate-limit store
        // failure counter), 3 observable gauges.
        published.Count.ShouldBe(18);

        published.Except(constants.Values, StringComparer.Ordinal).ShouldBeEmpty(
            "An instrument is published under a name that TraconDiagnostics does not declare. Declare it there.");
        constants.Values.Except(published, StringComparer.Ordinal).ShouldBeEmpty(
            "TraconDiagnostics declares an instrument name that the container never creates.");
    }

    [Fact]
    public void Every_instrument_publishes_at_least_the_attributes_it_always_had()
    {
        var instruments = PublishInstruments().ToDictionary(static instrument => instrument.Name, StringComparer.Ordinal);

        // The tag set is read from real measurements, so an instrument nobody measured would show an empty set and
        // look like a change. Every counter and histogram below is measured by RecordEverything.
        foreach (var instrument in instruments.Values.Where(static instrument => !instrument.Observable))
        {
            instrument.Tags.ShouldNotBeEmpty($"'{instrument.Name}' was not measured, so its attributes are unknown.");
        }

        // The tenant attribute is off by default; the agent version stays on.
        instruments[TraconDiagnostics.RunCounterName].Tags.Contains(TraconDiagnostics.Tags.AgentVersion).ShouldBeTrue();
        instruments[TraconDiagnostics.RunCounterName].Tags.Contains(TraconDiagnostics.Tags.TenantId).ShouldBeFalse();
        instruments[TraconDiagnostics.JobCounterName].Tags.Contains(TraconDiagnostics.Tags.TenantId).ShouldBeFalse();
    }

    private static List<string> Lines()
    {
        var lines = new List<string>
        {
            $"source {TraconDiagnostics.ActivitySourceName}",
            $"meter {TraconDiagnostics.MeterName}",
        };

        foreach (var (name, value) in DiagnosticsConstants(typeof(TraconDiagnostics)))
        {
            if (name.EndsWith("ActivityName", StringComparison.Ordinal))
            {
                lines.Add($"span {value}");
            }
            else if (name.EndsWith("ToolName", StringComparison.Ordinal))
            {
                lines.Add($"tool {value}");
            }
        }

        foreach (var (_, value) in DiagnosticsConstants(typeof(TraconDiagnostics.Tags)))
        {
            lines.Add($"attribute {value}");
        }

        foreach (var instrument in PublishInstruments())
        {
            var unit = string.IsNullOrEmpty(instrument.Unit) ? string.Empty : $" unit={instrument.Unit}";
            var tags = instrument.Observable ? string.Empty : $" attributes={string.Join(',', instrument.Tags.Order(StringComparer.Ordinal))}";

            lines.Add($"instrument {instrument.Name} {instrument.Kind}{unit}{tags}");
        }

        return lines;
    }

    private static Dictionary<string, string> InstrumentConstants()
        => DiagnosticsConstants(typeof(TraconDiagnostics))
            .Where(static pair => !string.Equals(pair.Key, nameof(TraconDiagnostics.ActivitySourceName), StringComparison.Ordinal)
                && !string.Equals(pair.Key, nameof(TraconDiagnostics.MeterName), StringComparison.Ordinal)
                && !pair.Key.EndsWith("ActivityName", StringComparison.Ordinal)
                && !pair.Key.EndsWith("ToolName", StringComparison.Ordinal))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);

    private static Dictionary<string, string> DiagnosticsConstants(Type type)
        => type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(static field => field.IsLiteral && field.FieldType == typeof(string))
            .ToDictionary(static field => field.Name, static field => (string)field.GetRawConstantValue()!, StringComparer.Ordinal);

    /// <summary>
    /// Builds a container the way a host does, turns the gauges on, makes every counter and histogram measure once,
    /// and reads what the listener saw. Sorted by name.
    /// </summary>
    private static List<Published> PublishInstruments()
    {
        using var meterFactory = new TestMeterFactory();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Tracon:Observability:EnableQuotaUsageGauge"] = "true",
                ["Tracon:Observability:EnableJobQueueDepthGauge"] = "true",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IMeterFactory>(meterFactory);
        services.AddTracon(configuration.GetSection(TraconOptions.SectionName));

        using var provider = services.BuildServiceProvider();

        // The gauges are created by hosted services; resolving them builds the instruments without starting any loop.
        _ = provider.GetServices<IHostedService>().ToList();

        var published = new Dictionary<string, Published>(StringComparer.Ordinal);

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (!ReferenceEquals(instrument.Meter, meterFactory.Meter))
            {
                return;
            }

            var type = instrument.GetType();
            var kind = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
            var valueType = type.GenericTypeArguments[0];

            published[instrument.Name] = new Published(
                instrument.Name,
                $"{kind}<{ValueTypeNames.GetValueOrDefault(valueType, valueType.Name)}>",
                instrument.Unit,
                instrument.IsObservable);

            meterListener.EnableMeasurementEvents(instrument);
        };

        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => Remember(published, instrument, tags));
        listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) => Remember(published, instrument, tags));
        listener.Start();

        MeasureEverything(provider.GetRequiredService<TraconMetrics>());

        return [.. published.Values.OrderBy(static instrument => instrument.Name, StringComparer.Ordinal)];
    }

    private static void Remember(Dictionary<string, Published> published, Instrument instrument, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        if (!published.TryGetValue(instrument.Name, out var entry))
        {
            return;
        }

        foreach (var tag in tags)
        {
            entry.Tags.Add(tag.Key);
        }
    }

    /// <summary>One call per recording method, with every optional argument the default configuration passes.</summary>
    private static void MeasureEverything(TraconMetrics metrics)
    {
        // The agent version is passed by the caller while IncludeAgentVersionTag (default on) is on; the metrics do not know the option.
        metrics.RecordRun("agent-a", RunStatus.Completed, "tenant-a", "model-a", TimeSpan.FromSeconds(1), new RunUsage { InputTokens = 1, OutputTokens = 1 }, agentVersion: 1);
        metrics.RecordToolInvocation("tool-a", succeeded: true, TimeSpan.FromSeconds(1));
        metrics.RecordCost("agent-a", "model-a", "tenant-a", 1m, "USD");
        metrics.RecordJudgeCost("judge-a", "model-a", "tenant-a", 1m, "USD");
        metrics.RecordJudgeScore("judge-a", "agent-a", "tenant-a", 80);
        metrics.RecordModelCacheLookup("provider-a", "tenant-a", hit: true);
        metrics.RecordAgentSourceFailure("source-a", "list");
        metrics.RecordAuditWriteFailure("tenant-a", "approval.decision", "swallowed");
        metrics.RecordRunRecordingFailure("tenant-a", "start");
        metrics.RecordRateLimitStoreFailure("endpoint");
        metrics.RecordJob("default", "handler-a", JobStatus.Completed, "tenant-a", TimeSpan.FromSeconds(1));
    }

    private sealed record Published(string Name, string Kind, string? Unit, bool Observable)
    {
        public SortedSet<string> Tags { get; } = new(StringComparer.Ordinal);
    }
}
