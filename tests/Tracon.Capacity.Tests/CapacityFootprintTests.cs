using System.Text.Json;
using Tracon.CapacityDriver;

namespace Tracon.Capacity.Tests;

/// <summary>The trend statistics, the tenant ramp and the footprint verdicts, against series built to leak or not to.</summary>
/// <remarks>
/// <para>
/// Every expectation for the statistics was computed apart from the implementation, with the standard library's
/// error function in another language. A test that asks the code for its own answer proves only that it agrees
/// with itself.
/// </para>
/// <para>
/// The verdict tests are the proof that the analysis can be red. A soak whose analysis cannot fail is not evidence
/// of anything; each test below builds the series a particular defect would leave, and one builds the series of a
/// healthy process.
/// </para>
/// </remarks>
public sealed class CapacityFootprintTests
{
    private const double MiB = 1024 * 1024;

    [Fact]
    public void Mann_Kendall_scores_a_strictly_rising_series_as_tau_one_and_highly_significant()
    {
        var test = TrendStatistics.MannKendall(Enumerable.Range(0, 10).Select(static i => (double)i).ToList());

        test.S.ShouldBe(45);
        test.Tau.ShouldBe(1.0);
        test.Z.ShouldBe(3.9354796403996297, 1e-6);
        test.PValue.ShouldBe(8.303070332644999e-05, 1e-6);
    }

    [Fact]
    public void Mann_Kendall_p_value_agrees_with_an_independent_calculation_on_a_noisy_rise()
    {
        var test = TrendStatistics.MannKendall([3, 1, 2, 5, 4, 6, 8, 7, 9, 12, 10, 11]);

        test.S.ShouldBe(54);
        test.Tau.ShouldBe(0.8181818181818182, 1e-9);
        test.Z.ShouldBe(3.634345051015831, 1e-6);
        test.PValue.ShouldBe(0.0002786876842340691, 1e-6);
    }

    [Fact]
    public void Mann_Kendall_corrects_the_variance_for_ties()
    {
        // Two flat runs of three: S = 9, and the tie correction shrinks the variance, so p is 0.0809 and not the
        // 0.0113 that an uncorrected variance would give.
        var test = TrendStatistics.MannKendall([1, 1, 1, 2, 2, 2]);

        test.S.ShouldBe(9);
        test.PValue.ShouldBe(0.08085559837005228, 1e-6);
    }

    [Fact]
    public void Mann_Kendall_on_a_constant_series_has_no_trend()
    {
        var test = TrendStatistics.MannKendall([5, 5, 5, 5, 5, 5, 5, 5]);

        test.S.ShouldBe(0);
        test.Tau.ShouldBe(0);
        test.PValue.ShouldBe(1.0);
    }

    [Fact]
    public void Mann_Kendall_sees_a_falling_series_as_negative()
    {
        var test = TrendStatistics.MannKendall([10, 9, 8, 7, 6, 5]);

        test.Tau.ShouldBe(-1.0);
        test.PValue.ShouldBe(0.008534920414227076, 1e-6);
    }

    [Fact]
    public void Mann_Kendall_with_fewer_than_three_points_refuses_to_claim_anything()
    {
        TrendStatistics.MannKendall([1, 2]).PValue.ShouldBe(1.0);
        TrendStatistics.MannKendall([]).PValue.ShouldBe(1.0);
    }

    [Fact]
    public void Theil_Sen_recovers_the_slope_through_an_outlier_that_would_drag_a_least_squares_fit()
    {
        double[] x = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
        double[] y = [0, 2, 4, 6, 8, 10, 12, 14, 16, 1000];

        TrendStatistics.TheilSen(x, y).ShouldBe(2.0, 1e-9);
    }

    [Fact]
    public void Median_of_even_and_odd_sets()
    {
        TrendStatistics.Median([3, 1, 2]).ShouldBe(2);
        TrendStatistics.Median([4, 1, 3, 2]).ShouldBe(2.5);
        double.IsNaN(TrendStatistics.Median([])).ShouldBeTrue();
    }

    [Fact]
    public void A_ramp_gives_each_joining_tenant_its_runs_then_holds_for_the_stage_duration()
    {
        var clock = 0.0;
        var spec = new TenantRampSpec
        {
            Stages =
            [
                new TenantRampStage { Tenants = 2, RunsPerNewTenant = 3, HoldSeconds = 10 },
                new TenantRampStage { Tenants = 5, RunsPerNewTenant = 2, HoldSeconds = 10 },
            ],
        };
        var schedule = new TenantRampSchedule(spec, () => clock);

        var tickets = new List<RampTicket>();

        for (var i = 0; i < 6; i++)
        {
            tickets.Add(schedule.Next()!.Value);
        }

        tickets.Select(static t => t.Tenant).ShouldBe(
            ["ramp-00000", "ramp-00000", "ramp-00000", "ramp-00001", "ramp-00001", "ramp-00001"]);
        tickets.ShouldAllBe(t => string.Equals(t.Phase, RampPhase.Ramp, StringComparison.Ordinal) && t.Stage == 0);

        // The last joiner has been handed out, so the plateau has begun and draws only from the two tenants.
        for (var i = 0; i < 50; i++)
        {
            clock += 0.1;
            var hold = schedule.Next()!.Value;

            hold.Phase.ShouldBe(RampPhase.Hold);
            hold.Stage.ShouldBe(0);
            (hold.Tenant is "ramp-00000" or "ramp-00001").ShouldBeTrue(hold.Tenant);
        }

        // Past the plateau, stage 1: three new tenants, two runs each, ids continue where stage 0 stopped.
        clock = 20;
        var joiners = Enumerable.Range(0, 6).Select(_ => schedule.Next()!.Value).ToList();

        joiners.Select(static t => t.Tenant).ShouldBe(
            ["ramp-00002", "ramp-00002", "ramp-00003", "ramp-00003", "ramp-00004", "ramp-00004"]);
        joiners.ShouldAllBe(t => string.Equals(t.Phase, RampPhase.Ramp, StringComparison.Ordinal) && t.Stage == 1);

        // Plateau of stage 1 may now draw from all five, and ends after ten seconds.
        var seen = new HashSet<string>(StringComparer.Ordinal);

        while (schedule.Next() is { } ticket)
        {
            ticket.Stage.ShouldBe(1);
            ticket.Phase.ShouldBe(RampPhase.Hold);
            seen.Add(ticket.Tenant);
            clock += 0.01;
        }

        seen.Count.ShouldBe(5);
        schedule.Phase.ShouldBe(RampPhase.Drain);
        schedule.Next().ShouldBeNull();
    }

    [Fact]
    public void A_ramp_records_where_every_phase_began_and_ended_without_gaps_or_overlap()
    {
        var clock = 0.0;
        var spec = new TenantRampSpec
        {
            Stages =
            [
                new TenantRampStage { Tenants = 2, RunsPerNewTenant = 1, HoldSeconds = 5 },
                new TenantRampStage { Tenants = 4, RunsPerNewTenant = 1, HoldSeconds = 5 },
            ],
        };
        var schedule = new TenantRampSchedule(spec, () => clock);

        while (schedule.Next() is not null)
        {
            clock += 0.5;
        }

        schedule.BeginDrain();
        clock += 1;
        schedule.BeginIdle();
        clock += 3;
        schedule.End();

        var marks = schedule.Marks();

        marks.Select(static m => (m.Stage, m.Phase)).ShouldBe(
        [
            (0, RampPhase.Ramp), (0, RampPhase.Hold), (1, RampPhase.Ramp), (1, RampPhase.Hold),
            (1, RampPhase.Drain), (1, RampPhase.Idle),
        ]);

        for (var i = 1; i < marks.Count; i++)
        {
            marks[i].StartSeconds.ShouldBe(marks[i - 1].EndSeconds);
            marks[i].EndSeconds.ShouldBeGreaterThanOrEqualTo(marks[i].StartSeconds);
        }

        marks[1].Tenants.ShouldBe(2);
        marks[3].Tenants.ShouldBe(4);
        marks[3].PreviousTenants.ShouldBe(2);
    }

    [Fact]
    public void Ramp_tenant_ids_are_well_formed_and_already_canonical()
    {
        var spec = new TenantRampSpec { TenantPrefix = "ramp-" };

        foreach (var index in new[] { 0, 1, 9, 10, 4_999, 99_999 })
        {
            var id = spec.TenantAt(index);

            id.Length.ShouldBeLessThanOrEqualTo(64);
            id.All(static c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '-' or '_' or '.').ShouldBeTrue(id);
            string.Equals(id, id.ToLowerInvariant(), StringComparison.Ordinal).ShouldBeTrue(id);
        }

        string.Equals(spec.TenantAt(0), spec.TenantAt(1), StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void A_ramp_that_does_not_climb_or_uses_a_non_canonical_prefix_cannot_be_measured()
    {
        var flat = new TenantRampSpec
        {
            Stages = [new TenantRampStage { Tenants = 10 }, new TenantRampStage { Tenants = 10 }],
        };

        flat.Validate().ShouldContain(p => p.Contains("greater than the stage before", StringComparison.Ordinal));

        var upper = new TenantRampSpec { TenantPrefix = "Ramp-", Stages = [new TenantRampStage { Tenants = 2 }] };

        upper.Validate().ShouldContain(p => p.Contains("lower-case", StringComparison.Ordinal));
        new TenantRampSpec { Stages = [] }.Validate().ShouldNotBeEmpty();
    }

    [Fact]
    public void A_cell_with_a_ramp_takes_its_bound_from_the_ramp_and_refuses_a_second_one()
    {
        var spec = new CellSpec
        {
            RunId = "run",
            CellId = "cell",
            Profile = "tenant-ramp",
            Scenarios = [CapacityScenario.Buffered],
            BaseAddress = "http://127.0.0.1:1/tracon",
            OutputDirectory = "out",
            Schema = "capacity",
            Tenants = ["capacity-a", "capacity-b"],
            TenantRamp = new TenantRampSpec { Stages = [new TenantRampStage { Tenants = 2 }] },
        };

        spec.Validate().ShouldBeEmpty();

        spec.MeasureSeconds = 30;
        spec.Validate().ShouldContain(p => p.Contains("must be zero", StringComparison.Ordinal));
    }

    [Fact]
    public void A_healthy_process_with_noise_and_a_small_cost_per_tenant_is_green()
    {
        var run = Fixture.Build((metric, time, tenants, _) => metric switch
        {
            "host.workingSetBytes" => (400 * MiB) + (tenants * 20 * 1024) + Noise(time, 4 * MiB),
            "host.liveHeapBytes" => (120 * MiB) + (tenants * 8 * 1024),
            "host.osThreads" => 40 + (time % 7 < 1 ? 1 : 0),
            "host.activeTimers" => 6,
            "host.openDescriptors" => 90,
            "pg.connections" => 18,
            _ => null,
        });

        var analysis = FootprintAnalysis.Analyse("cell", run.Samples, run.Ramp, Options);

        analysis.Verdict.ShouldBe(FootprintVerdict.Green);
        analysis.Metrics.ShouldAllBe(m => !string.Equals(m.Verdict, FootprintVerdict.Red, StringComparison.Ordinal), string.Join("; ", analysis.Metrics.SelectMany(static m => m.Reasons)));

        var heap = analysis.Metrics.Single(static m => string.Equals(m.Name, "host.liveHeapBytes", StringComparison.Ordinal));

        // 8 KiB per tenant across 198 added tenants is 1.5 MiB, under the 2 MiB floor, so the step is "no change
        // above the floor"; what the tenants cost is read from the idle figure, which is exact here.
        heap.RetainedPerTenant.ShouldNotBeNull().ShouldBe(8 * 1024, 64);
    }

    [Fact]
    public void A_leak_that_follows_runs_inside_a_plateau_is_red_even_with_the_tenants_fixed()
    {
        // 1 MiB per second of resident set from the moment the first plateau starts, in every plateau.
        var run = Fixture.Build((metric, time, _, _) => string.Equals(metric, "host.workingSetBytes", StringComparison.Ordinal)
            ? (400 * MiB) + (time * 1.0 * MiB)
            : null);

        var analysis = FootprintAnalysis.Analyse("cell", run.Samples, run.Ramp, Options);
        var rss = analysis.Metrics.Single(static m => string.Equals(m.Name, "host.workingSetBytes", StringComparison.Ordinal));

        analysis.Verdict.ShouldBe(FootprintVerdict.Red);
        rss.Verdict.ShouldBe(FootprintVerdict.Red);
        rss.Trends.ShouldAllBe(t => string.Equals(t.Verdict, FootprintVerdict.Red, StringComparison.Ordinal));
        rss.Reasons.ShouldContain(r => r.Contains("inside a plateau", StringComparison.Ordinal));
    }

    [Fact]
    public void A_rise_that_is_gone_in_the_next_plateau_is_a_warm_up_and_is_never_red()
    {
        // Rises 0.8 MiB/s while stage 1's tenants are fixed, then is flat again in stage 2 at the same load: it did
        // not follow time or runs, which went on, so it is set aside and reported as a note.
        var run = Fixture.Build((metric, time, _, stage) => string.Equals(metric, "host.workingSetBytes", StringComparison.Ordinal)
            ? 400 * MiB + (stage == 1 ? (time - Fixture.Stage1HoldStart) * 0.8 * MiB : 0)
            : null);

        var analysis = FootprintAnalysis.Analyse("cell", run.Samples, run.Ramp, Options);
        var rss = analysis.Metrics.Single(static m => string.Equals(m.Name, "host.workingSetBytes", StringComparison.Ordinal));

        rss.Trends.Select(static t => t.Verdict).ShouldBe(
            [FootprintVerdict.Green, FootprintVerdict.Transient, FootprintVerdict.Green]);
        rss.Verdict.ShouldBe(FootprintVerdict.Green);
        rss.Notes.ShouldContain(n => n.Contains("warm-up", StringComparison.Ordinal));
    }

    [Fact]
    public void A_rise_in_the_last_plateau_cannot_be_set_aside_and_is_red()
    {
        var run = Fixture.Build((metric, time, _, stage) => string.Equals(metric, "host.workingSetBytes", StringComparison.Ordinal)
            ? 400 * MiB + (stage == 2 ? (time - Fixture.Stage2HoldStart) * 0.8 * MiB : 0)
            : null);

        var analysis = FootprintAnalysis.Analyse("cell", run.Samples, run.Ramp, Options);
        var rss = analysis.Metrics.Single(static m => string.Equals(m.Name, "host.workingSetBytes", StringComparison.Ordinal));

        rss.Trends.Select(static t => t.Verdict).ShouldBe(
            [FootprintVerdict.Green, FootprintVerdict.Green, FootprintVerdict.Red]);
        rss.Verdict.ShouldBe(FootprintVerdict.Red);
    }

    [Fact]
    public void A_count_that_rises_at_low_cardinality_and_stays_flat_at_ten_times_the_tenants_is_a_warm_up()
    {
        // A thread pool finding its size: 10 -> 14 between stage 0 and stage 1, then 14 at 200 tenants.
        var run = Fixture.Build((metric, _, _, stage) => string.Equals(metric, "host.osThreads", StringComparison.Ordinal) ? (stage == 0 ? 10 : 14) : null);

        var analysis = FootprintAnalysis.Analyse("cell", run.Samples, run.Ramp, Options);
        var threads = analysis.Metrics.Single(static m => string.Equals(m.Name, "host.osThreads", StringComparison.Ordinal));

        threads.Steps.Select(static s => s.Verdict).ShouldBe([FootprintVerdict.Transient, FootprintVerdict.Green]);
        threads.Verdict.ShouldBe(FootprintVerdict.Green);
        threads.Notes.ShouldContain(n => n.Contains("not a resource per tenant", StringComparison.Ordinal));
    }

    [Fact]
    public void A_timer_count_that_rises_with_the_tenants_is_red_although_it_is_flat_inside_every_plateau()
    {
        var run = Fixture.Build((metric, _, tenants, _) => string.Equals(metric, "host.activeTimers", StringComparison.Ordinal) ? 6 + tenants : null);

        var analysis = FootprintAnalysis.Analyse("cell", run.Samples, run.Ramp, Options);
        var timers = analysis.Metrics.Single(static m => string.Equals(m.Name, "host.activeTimers", StringComparison.Ordinal));

        timers.Verdict.ShouldBe(FootprintVerdict.Red);
        timers.Trends.ShouldAllBe(t => string.Equals(t.Verdict, FootprintVerdict.Green, StringComparison.Ordinal));
        timers.Steps.ShouldAllBe(s => string.Equals(s.Verdict, FootprintVerdict.Red, StringComparison.Ordinal));
        timers.Reasons.ShouldContain(r => r.Contains("should not follow the number of tenants", StringComparison.Ordinal));
    }

    [Fact]
    public void A_cost_per_tenant_that_climbs_from_step_to_step_is_red_as_super_linear()
    {
        // Heap grows with the square of the tenants: 2 -> 20 -> 200 costs far more than ten times as much per tenant.
        var run = Fixture.Build((metric, _, tenants, _) => string.Equals(metric, "host.liveHeapBytes", StringComparison.Ordinal) ? (100 * MiB) + (tenants * (double)tenants * 20 * 1024) : null);

        var analysis = FootprintAnalysis.Analyse("cell", run.Samples, run.Ramp, Options);
        var heap = analysis.Metrics.Single(static m => string.Equals(m.Name, "host.liveHeapBytes", StringComparison.Ordinal));

        heap.Verdict.ShouldBe(FootprintVerdict.Red);
        heap.Steps.Last().RatioToPrevious.ShouldNotBeNull().ShouldBeGreaterThan(1.5);
        heap.Reasons.ShouldContain(r => r.Contains("super-linear", StringComparison.Ordinal));
    }

    [Fact]
    public void A_cost_per_tenant_that_stays_constant_is_not_super_linear()
    {
        var run = Fixture.Build((metric, _, tenants, _) => string.Equals(metric, "host.liveHeapBytes", StringComparison.Ordinal) ? (100 * MiB) + (tenants * 256 * 1024.0) : null);

        var analysis = FootprintAnalysis.Analyse("cell", run.Samples, run.Ramp, Options);
        var heap = analysis.Metrics.Single(static m => string.Equals(m.Name, "host.liveHeapBytes", StringComparison.Ordinal));

        heap.Verdict.ShouldBe(FootprintVerdict.Green);
        heap.Steps.Last().PerTenant.ShouldBe(256 * 1024, 64);
        heap.Steps.Last().RatioToPrevious.ShouldNotBeNull().ShouldBe(1.0, 0.05);
    }

    [Fact]
    public void A_plateau_with_too_few_points_is_inconclusive_and_never_green()
    {
        // Collected every 25 s inside a 60 s plateau: two points, fewer than the six a verdict needs.
        var run = Fixture.Build((metric, _, _, _) => string.Equals(metric, "host.liveHeapBytes", StringComparison.Ordinal) ? 120 * MiB : null, collectEvery: 25);

        var analysis = FootprintAnalysis.Analyse("cell", run.Samples, run.Ramp, Options);
        var heap = analysis.Metrics.Single(static m => string.Equals(m.Name, "host.liveHeapBytes", StringComparison.Ordinal));

        heap.Trends.ShouldAllBe(t => string.Equals(t.Verdict, FootprintVerdict.Inconclusive, StringComparison.Ordinal));
        heap.Verdict.ShouldBe(FootprintVerdict.Inconclusive);
    }

    [Fact]
    public void A_leak_smaller_than_the_floor_is_not_called_and_the_floor_is_reported_beside_the_flat_verdict()
    {
        // 1 KiB per second is 60 KiB over a plateau: far below 16 MiB. The analysis must say so rather than claim
        // the process does not leak.
        var run = Fixture.Build((metric, time, _, _) => string.Equals(metric, "host.workingSetBytes", StringComparison.Ordinal) ? (400 * MiB) + (time * 1024) : null);

        var analysis = FootprintAnalysis.Analyse("cell", run.Samples, run.Ramp, Options);
        var rss = analysis.Metrics.Single(static m => string.Equals(m.Name, "host.workingSetBytes", StringComparison.Ordinal));

        rss.Trends.ShouldAllBe(t => string.Equals(t.Verdict, FootprintVerdict.Green, StringComparison.Ordinal));
        rss.Trends.ShouldAllBe(t => t.Floor >= 16 * MiB);
    }

    [Fact]
    public void A_missing_reading_stays_a_hole_in_the_series_and_is_never_a_zero()
    {
        var run = Fixture.Build((metric, _, _, _) => string.Equals(metric, "host.osThreads", StringComparison.Ordinal) ? 40 : null);

        // Drop every other thread reading, as a host that did not answer would.
        for (var i = 0; i < run.Samples.Count; i += 2)
        {
            run.Samples[i].Values.Remove("host.osThreads");
            run.Samples[i].Missing = ["host: no answer inside the deadline"];
        }

        var analysis = FootprintAnalysis.Analyse("cell", run.Samples, run.Ramp, Options);
        var threads = analysis.Metrics.Single(static m => string.Equals(m.Name, "host.osThreads", StringComparison.Ordinal));

        threads.Minimum.ShouldBe(40);
        analysis.Gaps.ShouldBeGreaterThan(0);

        var csv = FootprintReport.Csv(run.Samples);
        var firstRow = csv.Split('\n')[1];

        // Row 0 lost its reading: the cell is empty, not 0.
        firstRow.TrimEnd('\r').EndsWith(',').ShouldBeTrue(firstRow);
    }

    [Fact]
    public void The_written_analysis_reads_back_and_names_the_verdict()
    {
        var run = Fixture.Build((metric, time, _, _) => string.Equals(metric, "host.workingSetBytes", StringComparison.Ordinal) ? (400 * MiB) + (time * 1.0 * MiB) : null);
        var directory = Directory.CreateTempSubdirectory("footprint-").FullName;

        try
        {
            File.WriteAllLines(
                Path.Combine(directory, FootprintReport.SamplesFile),
                run.Samples.Select(static s => JsonSerializer.Serialize(s, CapacityJson.Default.FootprintSample).ReplaceLineEndings("")));
            File.WriteAllText(
                Path.Combine(directory, FootprintReport.RampFile),
                JsonSerializer.Serialize(run.Ramp, CapacityJson.Default.RampRecord));

            FootprintReport.Exists(directory).ShouldBeTrue();

            var written = FootprintReport.Write(directory);

            written.Verdict.ShouldBe(FootprintVerdict.Red);

            var json = JsonSerializer.Deserialize(
                File.ReadAllText(Path.Combine(directory, "footprint.json")),
                CapacityJson.Default.FootprintAnalysisResult);

            json.ShouldNotBeNull().Verdict.ShouldBe(FootprintVerdict.Red);
            json.Metrics.Single(static m => string.Equals(m.Name, "host.workingSetBytes", StringComparison.Ordinal)).Kind.ShouldBe(MetricKind.Bytes);

            File.ReadAllText(Path.Combine(directory, "footprint.md")).ShouldContain("Verdict: **RED**");
            File.ReadAllText(Path.Combine(directory, "footprint.csv")).ShouldStartWith("offsetSeconds,stage,phase,tenants,issued,collected");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // Five-second windows: the fixture's plateaus are a minute long, so ten-second windows would leave too few
    // points to judge, which is its own (tested) outcome.
    private static FootprintAnalysisOptions Options { get; } = new() { BucketSeconds = 5 };

    private static double Noise(double time, double amplitude)
        => amplitude * Math.Sin(time * 1.7) * Math.Cos(time * 0.31);

    /// <summary>A three-stage ramp (2, 20, 200 tenants) sampled every second, with a reading per metric supplied by the test.</summary>
    private static class Fixture
    {
        // Stage k: ramp 5 s, plateau 60 s. Idle 20 s at the end.
        public const double Stage1HoldStart = 70;
        public const double Stage2HoldStart = 135;

        public static (List<FootprintSample> Samples, RampRecord Ramp) Build(
            Func<string, double, int, int, double?> reading,
            int collectEvery = 5)
        {
            int[] tenants = [2, 20, 200];
            var spec = new TenantRampSpec
            {
                Stages = tenants.Select(static t => new TenantRampStage { Tenants = t, RunsPerNewTenant = 2, HoldSeconds = 60 }).ToList(),
                IdleSeconds = 20,
                BucketSeconds = 5,
            };

            var ramp = new RampRecord { Spec = spec, BaselineTenants = 2 };
            double[] starts = [0, 65, 130];

            for (var stage = 0; stage < 3; stage++)
            {
                var previous = stage == 0 ? 0 : tenants[stage - 1];

                ramp.Marks.Add(new StageMark { Stage = stage, Phase = RampPhase.Ramp, Tenants = tenants[stage], PreviousTenants = previous, StartSeconds = starts[stage], EndSeconds = starts[stage] + 5 });
                ramp.Marks.Add(new StageMark { Stage = stage, Phase = RampPhase.Hold, Tenants = tenants[stage], PreviousTenants = previous, StartSeconds = starts[stage] + 5, EndSeconds = starts[stage] + 65, IssuedAtStart = stage * 1000, IssuedAtEnd = (stage * 1000) + 600 });
            }

            ramp.Marks.Add(new StageMark { Stage = 2, Phase = RampPhase.Idle, Tenants = 200, PreviousTenants = 20, StartSeconds = 195, EndSeconds = 215 });

            var samples = new List<FootprintSample>();

            for (var second = 0; second <= 215; second++)
            {
                var (stage, joined, phase) = At(second, tenants, starts);
                var sample = new FootprintSample
                {
                    CellId = "cell",
                    OffsetSeconds = second,
                    Stage = stage,
                    Phase = phase,
                    Tenants = joined,
                    Collected = second % collectEvery == 0,
                };

                foreach (var name in new[]
                {
                    "host.workingSetBytes", "host.osThreads", "host.activeTimers", "host.openDescriptors",
                    "pg.connections", "host.liveHeapBytes",
                })
                {
                    if (string.Equals(name, "host.liveHeapBytes", StringComparison.Ordinal) && !sample.Collected)
                    {
                        continue;
                    }

                    if (reading(name, second, joined, stage) is { } value)
                    {
                        sample.Values[name] = value;
                    }
                }

                samples.Add(sample);
            }

            return (samples, ramp);
        }

        private static (int Stage, int Joined, string Phase) At(double second, int[] tenants, double[] starts)
        {
            if (second >= 195)
            {
                return (2, tenants[2], RampPhase.Idle);
            }

            for (var stage = 2; stage >= 0; stage--)
            {
                if (second < starts[stage])
                {
                    continue;
                }

                var previous = stage == 0 ? 0 : tenants[stage - 1];

                if (second < starts[stage] + 5)
                {
                    var joined = previous + (int)((tenants[stage] - previous) * ((second - starts[stage]) / 5));
                    return (stage, joined, RampPhase.Ramp);
                }

                return (stage, tenants[stage], RampPhase.Hold);
            }

            return (0, 0, RampPhase.Ramp);
        }
    }
}
