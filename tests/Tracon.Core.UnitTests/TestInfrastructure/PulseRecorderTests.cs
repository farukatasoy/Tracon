namespace Tracon.Core.UnitTests.TestInfrastructure;

/// <summary>
/// The heartbeat that tells a failed wait whether the test process stood still
/// (<c>tests/Shared/Evidence/PulseRecorder.cs</c>).
/// </summary>
/// <remarks>
/// The recorder measures time on a clock it is given, so these cases move a <see cref="ManualClock"/> by hand and wait for
/// nothing.
/// </remarks>
public sealed class PulseRecorderTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan Threshold = TimeSpan.FromMilliseconds(250);

    [Fact]
    public void A_pause_longer_than_the_threshold_is_written_with_its_time()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));
        var lines = new List<string>();
        var recorder = new PulseRecorder(clock, Interval, Threshold, lines.Add);

        clock.Advance(Interval);
        recorder.Beat();
        clock.Advance(TimeSpan.FromMilliseconds(1500));
        recorder.Beat();

        lines.ShouldHaveSingleItem();
        lines[0].ShouldStartWith("2026-11-10T12:00:01.600Z process stall 1400 ms");
    }

    [Fact]
    public void A_heartbeat_on_time_writes_nothing()
    {
        var clock = new ManualClock();
        var lines = new List<string>();
        var recorder = new PulseRecorder(clock, Interval, Threshold, lines.Add);

        for (var beat = 0; beat < 50; beat++)
        {
            clock.Advance(Interval);
            recorder.Beat();
        }

        lines.ShouldBeEmpty();
    }

    [Fact]
    public void A_delay_just_under_the_threshold_writes_nothing_and_a_delay_at_it_writes()
    {
        var clock = new ManualClock();
        var lines = new List<string>();
        var recorder = new PulseRecorder(clock, Interval, Threshold, lines.Add);

        clock.Advance(Interval + Threshold - TimeSpan.FromMilliseconds(1));
        recorder.Beat();
        lines.ShouldBeEmpty();

        clock.Advance(Interval + Threshold);
        recorder.Beat();
        lines.ShouldHaveSingleItem();
    }

    [Fact]
    public void A_pool_work_item_that_started_late_is_written_when_it_arrives()
    {
        var clock = new ManualClock();
        var lines = new List<string>();
        var recorder = new PulseRecorder(clock, Interval, Threshold, lines.Add);

        var probe = recorder.PoolProbeQueued();
        clock.Advance(TimeSpan.FromMilliseconds(800));
        recorder.PoolProbeStarted(probe);
        recorder.Flush();

        lines.ShouldHaveSingleItem();
        lines[0].ShouldContain("thread pool delay 800 ms");
    }

    [Fact]
    public void A_pool_work_item_that_has_not_started_is_written_once_while_it_waits()
    {
        var clock = new ManualClock();
        var lines = new List<string>();
        var recorder = new PulseRecorder(clock, Interval, Threshold, lines.Add);

        recorder.PoolProbeQueued();
        clock.Advance(TimeSpan.FromMilliseconds(400));
        recorder.Flush();
        clock.Advance(TimeSpan.FromMilliseconds(400));
        recorder.Flush();

        lines.ShouldHaveSingleItem();
        lines[0].ShouldContain("thread pool delay at least 400 ms");
        lines[0].ShouldContain("still waiting");
    }

    [Fact]
    public void A_starved_pool_that_recovers_is_one_episode_in_two_lines()
    {
        var clock = new ManualClock();
        var lines = new List<string>();
        var recorder = new PulseRecorder(clock, Interval, Threshold, lines.Add);
        var probes = new List<long>();

        for (var queued = 0; queued < 8; queued++)
        {
            probes.Add(recorder.PoolProbeQueued());
            clock.Advance(Interval);
            recorder.Flush();
        }

        foreach (var probe in probes)
        {
            recorder.PoolProbeStarted(probe);
        }

        recorder.Flush();

        lines.Count.ShouldBe(2);
        lines[0].ShouldContain("still waiting");
        lines[1].ShouldContain("work items waited");
        lines[1].ShouldContain("thread pool delay 800 ms");
    }

    [Fact]
    public void The_window_reports_the_worst_delays_inside_it_and_not_before_it()
    {
        var clock = new ManualClock();
        var recorder = new PulseRecorder(clock, Interval, Threshold, static _ => { });

        clock.Advance(TimeSpan.FromMilliseconds(2100));
        recorder.Beat();
        var windowStart = recorder.Now + 1;

        clock.Advance(Interval + TimeSpan.FromMilliseconds(300));
        recorder.Beat();

        var probe = recorder.PoolProbeQueued();
        clock.Advance(TimeSpan.FromMilliseconds(40));
        recorder.PoolProbeStarted(probe);
        recorder.Flush();

        var window = recorder.Window(windowStart);

        window.LongestStall.ShouldBe(TimeSpan.FromMilliseconds(300));
        window.LongestPoolDelay.ShouldBe(TimeSpan.FromMilliseconds(40));
        window.Measurements.ShouldBe(2);
    }

    [Fact]
    public void A_pool_work_item_that_is_still_waiting_counts_in_the_window()
    {
        var clock = new ManualClock();
        var recorder = new PulseRecorder(clock, Interval, Threshold, static _ => { });
        var windowStart = recorder.Now;

        recorder.PoolProbeQueued();
        clock.Advance(TimeSpan.FromSeconds(3));

        recorder.Window(windowStart).LongestPoolDelay.ShouldBe(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void A_window_with_no_measurement_says_so()
    {
        var clock = new ManualClock();
        var recorder = new PulseRecorder(clock, Interval, Threshold, static _ => { });

        clock.Advance(Interval);
        recorder.Beat();

        recorder.Window(recorder.Now + 1).Measurements.ShouldBe(0);
    }

    [Fact]
    public void A_process_that_stalls_all_the_time_cannot_fill_the_sink()
    {
        var clock = new ManualClock();
        var lines = new List<string>();
        var recorder = new PulseRecorder(clock, Interval, Threshold, lines.Add);

        for (var beat = 0; beat < PulseRecorder.SinkLineLimit + 100; beat++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            recorder.Beat();
        }

        lines.Count.ShouldBe(PulseRecorder.SinkLineLimit);
        lines[^1].ShouldContain("line limit");
    }

    [Fact]
    public void The_ring_keeps_the_newest_measurements_when_it_is_full()
    {
        var clock = new ManualClock();
        var recorder = new PulseRecorder(clock, Interval, Threshold, static _ => { });

        clock.Advance(TimeSpan.FromSeconds(5));
        recorder.Beat();

        for (var beat = 0; beat < PulseRecorder.RingCapacity; beat++)
        {
            clock.Advance(Interval);
            recorder.Beat();
        }

        recorder.Window(0).LongestStall.ShouldBe(TimeSpan.Zero);
    }
}
