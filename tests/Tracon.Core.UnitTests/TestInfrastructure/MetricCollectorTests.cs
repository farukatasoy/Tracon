using System.Diagnostics.Metrics;

namespace Tracon.Core.UnitTests.TestInfrastructure;

/// <summary>
/// The metric listener the test projects link (<c>tests/Shared/Telemetry/MetricCollector.cs</c>).
/// </summary>
/// <remarks>
/// Many assertions across the suite read their numbers through it, so a collector that records the wrong meter, drops a
/// value type or keeps a stale tag span turns a real failure green or a green test red somewhere far from here. Each case
/// pins one thing the collector promises.
/// </remarks>
public sealed class MetricCollectorTests
{
    [Fact]
    public void A_collector_bound_to_a_meter_instance_ignores_another_meter_with_the_same_name()
    {
        using var mine = new Meter("tracon.test.collector.shared-name");
        using var theirs = new Meter("tracon.test.collector.shared-name");
        using var collector = new MetricCollector(mine);

        mine.CreateCounter<long>("probe.count").Add(1);
        theirs.CreateCounter<long>("probe.count").Add(100);

        collector.Values("probe.count").ShouldBe([1.0]);
    }

    [Fact]
    public void The_tags_of_a_measurement_are_copied_out_of_the_callback()
    {
        using var meter = new Meter("tracon.test.collector.tags");
        using var collector = new MetricCollector(meter);

        meter.CreateCounter<long>("probe.count").Add(1, new KeyValuePair<string, object?>("lane", "media"), new KeyValuePair<string, object?>("attempt", 2));

        var tags = collector.Tags("probe.count").ShouldHaveSingleItem();

        tags["lane"].ShouldBe("media");
        tags["attempt"].ShouldBe(2);
    }

    [Fact]
    public void Every_value_type_an_instrument_can_measure_is_recorded_as_a_double_with_its_type()
    {
        using var meter = new Meter("tracon.test.collector.types");
        using var collector = new MetricCollector(meter);

        meter.CreateCounter<byte>("probe.byte").Add(3);
        meter.CreateCounter<short>("probe.short").Add(3);
        meter.CreateCounter<int>("probe.int").Add(3);
        meter.CreateCounter<long>("probe.long").Add(3);
        meter.CreateCounter<float>("probe.float").Add(3);
        meter.CreateCounter<double>("probe.double").Add(3);
        meter.CreateCounter<decimal>("probe.decimal").Add(3);

        var recorded = collector.Snapshot().ToDictionary(static measurement => measurement.Instrument, StringComparer.Ordinal);

        recorded.Count.ShouldBe(7);
        recorded.Values.ShouldAllBe(static measurement => measurement.Value == 3.0);
        recorded["probe.byte"].ValueType.ShouldBe(typeof(byte));
        recorded["probe.short"].ValueType.ShouldBe(typeof(short));
        recorded["probe.int"].ValueType.ShouldBe(typeof(int));
        recorded["probe.long"].ValueType.ShouldBe(typeof(long));
        recorded["probe.float"].ValueType.ShouldBe(typeof(float));
        recorded["probe.double"].ValueType.ShouldBe(typeof(double));
        recorded["probe.decimal"].ValueType.ShouldBe(typeof(decimal));
    }

    [Fact]
    public void A_scrape_forgets_what_was_recorded_and_reports_what_the_observable_instruments_say_now()
    {
        using var meter = new Meter("tracon.test.collector.scrape");
        var level = 4L;
        meter.CreateObservableGauge("probe.level", () => level);
        var counter = meter.CreateCounter<long>("probe.count");
        using var collector = new MetricCollector(meter);

        counter.Add(1);

        collector.Scrape("probe.level").ShouldHaveSingleItem().Value.ShouldBe(4.0);
        collector.Snapshot("probe.count").ShouldBeEmpty("a scrape starts from nothing, so an earlier measurement cannot answer it");

        level = 9;

        collector.Scrape("probe.level").ShouldHaveSingleItem().Value.ShouldBe(9.0);
    }

    [Fact]
    public void A_drain_returns_everything_in_arrival_order_and_forgets_it()
    {
        using var meter = new Meter("tracon.test.collector.drain");
        var counter = meter.CreateCounter<long>("probe.count");
        using var collector = new MetricCollector(meter);

        counter.Add(1);
        counter.Add(2);

        collector.Drain().Select(static measurement => measurement.Value).ShouldBe([1.0, 2.0]);
        collector.Snapshot().ShouldBeEmpty();

        counter.Add(3);

        collector.Drain().ShouldHaveSingleItem().Value.ShouldBe(3.0);
    }

    [Fact]
    public void A_collector_selected_by_instrument_name_records_that_instrument_on_every_meter_and_nothing_else()
    {
        using var first = new Meter("tracon.test.collector.by-name.first");
        using var second = new Meter("tracon.test.collector.by-name.second");
        using var collector = MetricCollector.ForInstruments("probe.wanted");

        first.CreateCounter<long>("probe.wanted").Add(1);
        second.CreateCounter<long>("probe.wanted").Add(2);
        first.CreateCounter<long>("probe.other").Add(4);

        collector.Values("probe.wanted").Order().ShouldBe([1.0, 2.0]);
        collector.Snapshot("probe.other").ShouldBeEmpty();
    }

    [Fact]
    public void A_collector_stops_recording_when_it_is_disposed()
    {
        using var meter = new Meter("tracon.test.collector.disposed");
        var counter = meter.CreateCounter<long>("probe.count");
        var collector = new MetricCollector(meter);

        counter.Add(1);
        collector.Dispose();
        counter.Add(2);

        collector.Values("probe.count").ShouldBe([1.0]);
    }

    [Fact]
    public async Task A_wait_returns_the_first_measurement_that_arrives_after_it_started()
    {
        using var meter = new Meter("tracon.test.collector.wait");
        var counter = meter.CreateCounter<long>("probe.count");
        using var collector = new MetricCollector(meter);

        // The first probe runs inside the call, before anything was recorded, so the measurement below can only be found by a later one.
        var wait = collector.WaitForAsync("probe.count");

        counter.Add(7);

        (await wait).Value.ShouldBe(7.0);
    }

    [Fact]
    public async Task A_wait_with_a_predicate_skips_the_measurements_that_do_not_match()
    {
        using var meter = new Meter("tracon.test.collector.wait-tag");
        var counter = meter.CreateCounter<long>("probe.count");
        using var collector = new MetricCollector(meter);

        counter.Add(1, new KeyValuePair<string, object?>("lane", "default"));
        counter.Add(2, new KeyValuePair<string, object?>("lane", "media"));

        var matching = await collector.WaitForAsync("probe.count", static measurement => measurement.HasTag("lane", "media"));

        matching.Value.ShouldBe(2.0);
    }

    [Fact]
    public async Task A_wait_for_a_measurement_that_never_comes_times_out_and_names_the_instrument()
    {
        using var meter = new Meter("tracon.test.collector.wait-timeout");
        using var collector = new MetricCollector(meter);

        var timeout = await Should.ThrowAsync<TimeoutException>(
            () => collector.WaitForAsync("probe.never", timeout: TimeSpan.FromMilliseconds(100)));

        timeout.Message.ShouldContain("probe.never");
    }
}
