using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Diagnostics;

/// <summary>
/// Phase 133.2: the lane tag is the one value Tracon does not control, so
/// its cardinality is bounded. These tests pin both ends of that guard — the
/// overflow value, and the promise that a named lane keeps its name.
/// </summary>
public sealed class JobLaneCardinalityTests
{
    [Fact]
    public void A_lane_beyond_the_limit_is_written_as_other()
    {
        using var harness = new MetricsHarness(maxLanes: 2);

        harness.Record("alpha");
        harness.Record("beta");
        harness.Record("gamma");

        harness.Lanes().ShouldBe(["alpha", "beta", "other"]);
    }

    [Fact]
    public void A_lane_that_earned_its_name_keeps_it_after_the_limit_is_reached()
    {
        using var harness = new MetricsHarness(maxLanes: 2);

        harness.Record("alpha");
        harness.Record("beta");
        harness.Record("gamma");

        // The set only ever grows. If it shrank, "alpha" would be written under
        // its own name one day and as "other" the next, and the resulting
        // series would be unreadable on a dashboard.
        harness.Record("alpha");

        harness.Lanes().ShouldBe(["alpha", "beta", "other", "alpha"]);
    }

    [Fact]
    public void Repeating_the_same_lane_does_not_consume_extra_budget()
    {
        using var harness = new MetricsHarness(maxLanes: 2);

        harness.Record("alpha");
        harness.Record("alpha");
        harness.Record("alpha");
        harness.Record("beta");

        harness.Lanes().ShouldBe(["alpha", "alpha", "alpha", "beta"]);
    }

    [Fact]
    public void The_default_limit_is_64_distinct_lanes()
    {
        using var harness = new MetricsHarness(maxLanes: null);

        for (var index = 0; index < 64; index++)
        {
            harness.Record($"lane-{index}");
        }

        harness.Record("one-too-many");

        harness.Lanes().Count(static lane => string.Equals(lane, "other", StringComparison.Ordinal)).ShouldBe(1);
        harness.Lanes()[63].ShouldBe("lane-63");
    }

    [Fact]
    public void A_missing_options_monitor_still_applies_the_default_limit()
    {
        // TraconMetrics can be constructed with no options at all; the
        // guard must not fall back to "unbounded".
        using var factory = new TestMeterFactory();
        using var collector = new LaneCollector(factory.Meter);
        using var metrics = new TraconMetrics(factory);

        for (var index = 0; index < 70; index++)
        {
            metrics.RecordJob($"lane-{index}", JobHandlerKeys.AgentBatch, JobStatus.Completed, "t", TimeSpan.Zero);
        }

        collector.Lanes.Count(static lane => string.Equals(lane, "other", StringComparison.Ordinal)).ShouldBe(6);
    }

    [Fact]
    public void Concurrent_completions_never_push_the_set_past_the_limit()
    {
        using var harness = new MetricsHarness(maxLanes: 8);

        // 32 threads racing on 32 distinct lanes for 8 slots. The guard reserves
        // a slot BEFORE recording the name, so exactly 8 lanes may be named and
        // the rest must overflow — no torn state, no over-admission.
        Parallel.For(0, 32, index => harness.Record($"lane-{index}"));

        var named = harness.Lanes()
            .Where(static lane => !string.Equals(lane, "other", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        named.Count.ShouldBe(8);
        harness.Lanes().Count.ShouldBe(32);
    }

    /// <summary>Records job measurements against a real <see cref="TraconMetrics"/> and reads back the lane tags.</summary>
    private sealed class MetricsHarness : IDisposable
    {
        private readonly TestMeterFactory _factory = new();
        private readonly LaneCollector _collector;
        private readonly TraconMetrics _metrics;

        public MetricsHarness(int? maxLanes)
        {
            _collector = new LaneCollector(_factory.Meter);

            var options = new TraconOptions();

            if (maxLanes is { } max)
            {
                options.Observability.MaxJobLaneCardinality = max;
            }

            _metrics = new TraconMetrics(_factory, new StaticOptionsMonitor<TraconOptions>(options));
        }

        public void Record(string lane)
            => _metrics.RecordJob(lane, JobHandlerKeys.AgentBatch, JobStatus.Completed, "tenant-a", TimeSpan.FromSeconds(1));

        public List<string> Lanes() => _collector.Lanes;

        public void Dispose()
        {
            _metrics.Dispose();
            _collector.Dispose();
            _factory.Dispose();
        }
    }

    /// <summary>Captures the lane tag of every <c>tracon.job.executions</c> measurement, in order.</summary>
    private sealed class LaneCollector : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentQueue<string> _lanes = new();

        public LaneCollector(Meter meter)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter, meter)
                    && string.Equals(instrument.Name, TraconDiagnostics.JobCounterName, StringComparison.Ordinal))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            {
                foreach (var tag in tags)
                {
                    if (string.Equals(tag.Key, TraconDiagnostics.Tags.Lane, StringComparison.Ordinal)
                        && tag.Value is string lane)
                    {
                        _lanes.Enqueue(lane);
                    }
                }
            });

            _listener.Start();
        }

        public List<string> Lanes => [.. _lanes];

        public void Dispose() => _listener.Dispose();
    }
}
