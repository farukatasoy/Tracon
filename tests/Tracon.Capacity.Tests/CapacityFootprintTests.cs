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
    public void A_single_jump_inside_the_last_plateau_is_a_note_and_not_a_leak_while_a_slope_of_the_same_size_is_red()
    {
        var fine = new FootprintAnalysisOptions { BucketSeconds = 2 };

        // 40 MiB at the middle of the last plateau, level before and after: a pool or a buffer finding its size.
        // The rank test counts this as a trend and Theil-Sen is positive, so only the two-halves check separates it.
        var jump = Fixture.Build((metric, time, _, _) => string.Equals(metric, "host.workingSetBytes", StringComparison.Ordinal) ? 400 * MiB + (time >= 171 ? 40 * MiB : 0) : null);
        var jumped = FootprintAnalysis.Analyse("cell", jump.Samples, jump.Ramp, fine)
            .Metrics.Single(static m => string.Equals(m.Name, "host.workingSetBytes", StringComparison.Ordinal));

        jumped.Trends.Last().Verdict.ShouldBe(FootprintVerdict.Jump);
        jumped.Verdict.ShouldBe(FootprintVerdict.Green);
        jumped.Notes.ShouldContain(n => n.Contains("one jump", StringComparison.Ordinal));

        // The same 40 MiB spread over the last plateau as a slope is a leak.
        var slope = Fixture.Build((metric, time, _, stage) => string.Equals(metric, "host.workingSetBytes", StringComparison.Ordinal)
            ? 400 * MiB + (stage == 2 ? (time - Fixture.Stage2HoldStart) * (40.0 / 60) * MiB : 0)
            : null);
        var sloped = FootprintAnalysis.Analyse("cell", slope.Samples, slope.Ramp, fine)
            .Metrics.Single(static m => string.Equals(m.Name, "host.workingSetBytes", StringComparison.Ordinal));

        sloped.Trends.Last().Verdict.ShouldBe(FootprintVerdict.Red);
        sloped.Verdict.ShouldBe(FootprintVerdict.Red);
    }

    [Fact]
    public void A_meter_per_tenant_is_a_cost_and_a_meter_per_run_is_a_leak()
    {
        // One announcement per tenant: the cost of the tenants, flat inside every plateau, not red.
        var perTenant = Fixture.Build((metric, _, tenants, _) => string.Equals(metric, "host.meterPublications", StringComparison.Ordinal) ? 150 + tenants : null);
        var tenantCost = FootprintAnalysis.Analyse("cell", perTenant.Samples, perTenant.Ramp, Options)
            .Metrics.Single(static m => string.Equals(m.Name, "host.meterPublications", StringComparison.Ordinal));

        tenantCost.Kind.ShouldBe(MetricKind.Objects);
        tenantCost.Verdict.ShouldBe(FootprintVerdict.Green);
        tenantCost.Steps.Last().PerTenant.ShouldBe(1.0, 0.01);

        // One announcement per second inside every plateau, with the tenants fixed: a meter made per run.
        var perRun = Fixture.Build((metric, time, _, _) => string.Equals(metric, "host.meterPublications", StringComparison.Ordinal) ? 150 + time : null);
        var runCost = FootprintAnalysis.Analyse("cell", perRun.Samples, perRun.Ramp, Options)
            .Metrics.Single(static m => string.Equals(m.Name, "host.meterPublications", StringComparison.Ordinal));

        runCost.Verdict.ShouldBe(FootprintVerdict.Red);
    }

    [Fact]
    public void A_connection_pool_that_moves_with_the_load_and_not_with_the_tenants_is_not_a_leak()
    {
        // The pool ends its plateaus at 9, 13 and 17 connections while the plateaus as a whole sit near 11: what the
        // rule for a plain count called a leak in a soak whose pool simply opened more connections under load. The
        // same readings under a plain count's name are the control: that rule is red on them.
        var run = Fixture.Build((metric, time, _, stage) => metric is "pg.connections" or "host.osThreads" ? PoolWave(time, stage) : null);

        var analysis = FootprintAnalysis.Analyse("cell", run.Samples, run.Ramp, Options);
        var pool = analysis.Metrics.Single(static m => string.Equals(m.Name, "pg.connections", StringComparison.Ordinal));
        var plain = analysis.Metrics.Single(static m => string.Equals(m.Name, "host.osThreads", StringComparison.Ordinal));

        plain.Verdict.ShouldBe(FootprintVerdict.Red, "the control: a plain count reads the pool's breathing as growth");

        pool.Kind.ShouldBe(MetricKind.Pooled);
        pool.Verdict.ShouldBe(FootprintVerdict.Green, string.Join("; ", pool.Reasons));
        pool.Steps.ShouldAllBe(static s => string.Equals(s.Verdict, FootprintVerdict.Green, StringComparison.Ordinal));
        pool.Levels.ShouldAllBe(static l => l.HasValue && l.Value > 10 && l.Value < 12, "a plateau stands for its median, not for its last third");
        pool.Breath.ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(8);
    }

    [Fact]
    public void A_timer_count_that_breathes_with_the_load_and_not_with_the_tenants_is_not_a_leak()
    {
        // The timers of the work in flight rise and fall with the load: in five soaks the same configuration was red in
        // one and green in the next. The same wave under a plain count's name is the control.
        var run = Fixture.Build((metric, time, _, stage) => metric is "host.activeTimers" or "host.osThreads" ? PoolWave(time, stage) : null);

        var analysis = FootprintAnalysis.Analyse("cell", run.Samples, run.Ramp, Options);
        var timers = analysis.Metrics.Single(static m => string.Equals(m.Name, "host.activeTimers", StringComparison.Ordinal));
        var plain = analysis.Metrics.Single(static m => string.Equals(m.Name, "host.osThreads", StringComparison.Ordinal));

        plain.Verdict.ShouldBe(FootprintVerdict.Red, "the control: a plain count reads the breathing as growth");

        timers.Kind.ShouldBe(MetricKind.Pooled);
        timers.Verdict.ShouldBe(FootprintVerdict.Green, string.Join("; ", timers.Reasons));
    }

    [Fact]
    public void A_timer_count_has_no_ceiling_to_fill_up()
    {
        // 18, 19 and 20 timers against a ramp that records a pool of 20: the connection pool's maximum says nothing about timers.
        var run = Fixture.Build((metric, _, _, stage) => string.Equals(metric, "host.activeTimers", StringComparison.Ordinal) ? 18 + stage : null);
        run.Ramp.MaxPoolSize = 20;

        var timers = FootprintAnalysis.Analyse("cell", run.Samples, run.Ramp, Options)
            .Metrics.Single(static m => string.Equals(m.Name, "host.activeTimers", StringComparison.Ordinal));

        timers.Verdict.ShouldBe(FootprintVerdict.Green, string.Join("; ", timers.Reasons));
    }

    [Fact]
    public void A_connection_pool_whose_lowest_reading_climbs_with_the_tenants_is_red()
    {
        // The same breathing with one more connection for every ten tenants: 0.2, 2 and 20 on top of it.
        var run = Fixture.Build((metric, time, tenants, stage) => string.Equals(metric, "pg.connections", StringComparison.Ordinal)
            ? PoolWave(time, stage) + (tenants / 10.0)
            : null);

        var pool = FootprintAnalysis.Analyse("cell", run.Samples, run.Ramp, Options)
            .Metrics.Single(static m => string.Equals(m.Name, "pg.connections", StringComparison.Ordinal));

        pool.Verdict.ShouldBe(FootprintVerdict.Red);
        pool.Steps.Last().Verdict.ShouldBe(FootprintVerdict.Red);
        pool.Reasons.ShouldContain(r => r.Contains("its lowest window rose too", StringComparison.Ordinal));
        pool.Troughs.Last().ShouldNotBeNull().ShouldBeGreaterThan(pool.Troughs[0]!.Value + 15);
    }

    [Fact]
    public void A_connection_pool_that_filled_for_the_load_but_went_back_down_is_not_a_leak()
    {
        // 8 connections, then 20 for most of the last plateau: the level is up by more than the pool breathes, but
        // the pool still touched 8 when the load let it. A connection held for a tenant does not go back.
        var run = Fixture.Build((metric, time, _, stage) => string.Equals(metric, "pg.connections", StringComparison.Ordinal)
            ? (stage == 2 && time >= 160 ? 20 : 8)
            : null);

        var pool = FootprintAnalysis.Analyse("cell", run.Samples, run.Ramp, Options)
            .Metrics.Single(static m => string.Equals(m.Name, "pg.connections", StringComparison.Ordinal));

        pool.Steps.Last().Delta.ShouldBeGreaterThan(pool.Breath!.Value);
        pool.Steps.Last().Verdict.ShouldBe(FootprintVerdict.Green);
        pool.Steps.Last().Note.ShouldContain("filled for the load");
        string.Equals(pool.Verdict, FootprintVerdict.Red, StringComparison.Ordinal).ShouldBeFalse(string.Join("; ", pool.Reasons));
    }

    [Fact]
    public void A_connection_pool_whose_plateau_reaches_the_limit_is_red_however_slowly_it_got_there()
    {
        // 18, 19 and 20 connections: each step is within the breathing, and the last plateau sits at a limit of 20.
        var readings = (Func<string, double, int, int, double?>)((metric, _, _, stage) => string.Equals(metric, "pg.connections", StringComparison.Ordinal) ? 18 + stage : null);

        var limited = Fixture.Build(readings);
        limited.Ramp.MaxPoolSize = 20;

        var pool = FootprintAnalysis.Analyse("cell", limited.Samples, limited.Ramp, Options)
            .Metrics.Single(static m => string.Equals(m.Name, "pg.connections", StringComparison.Ordinal));

        pool.Steps.ShouldAllBe(static s => string.Equals(s.Verdict, FootprintVerdict.Green, StringComparison.Ordinal));
        pool.Verdict.ShouldBe(FootprintVerdict.Red);
        pool.Reasons.ShouldContain(r => r.Contains("the pool is used up", StringComparison.Ordinal));

        // The same readings against a pool of a hundred, or against a ramp that did not record the limit, are flat.
        var roomy = Fixture.Build(readings);
        roomy.Ramp.MaxPoolSize = 100;
        var unknown = Fixture.Build(readings);

        foreach (var run in new[] { roomy, unknown })
        {
            FootprintAnalysis.Analyse("cell", run.Samples, run.Ramp, Options)
                .Metrics.Single(static m => string.Equals(m.Name, "pg.connections", StringComparison.Ordinal))
                .Verdict.ShouldBe(FootprintVerdict.Green);
        }

        // The limit travels in ramp.json; a file written before it was recorded has none.
        JsonSerializer.Deserialize(JsonSerializer.Serialize(limited.Ramp, CapacityJson.Default.RampRecord), CapacityJson.Default.RampRecord)
            .ShouldNotBeNull().MaxPoolSize.ShouldBe(20);
        JsonSerializer.Deserialize(JsonSerializer.Serialize(unknown.Ramp, CapacityJson.Default.RampRecord), CapacityJson.Default.RampRecord)
            .ShouldNotBeNull().MaxPoolSize.ShouldBeNull();
    }

    [Fact]
    public void A_series_for_every_tenant_is_a_cost_when_the_tenant_tag_is_on_and_a_leak_when_it_is_off()
    {
        // The run counter gets three series for each tenant: what the tag is documented to do. A series of an instrument
        // the tag does not touch, rising the same way, is not by design.
        static void Series(FootprintSample sample, Func<int, long> follow)
            => sample.InstrumentSeries = new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["Tracon/tracon.runs"] = 12 + follow(sample.Tenants),
                ["Tracon/tracon.agent.source_failures"] = 12 + follow(sample.Tenants),
            };

        var tagged = Fixture.Build((_, _, _, _) => null);
        tagged.Ramp.TenantTagged = true;
        tagged.Samples.ForEach(sample => Series(sample, tenants => 3L * tenants));

        var analysis = FootprintAnalysis.Analyse("cell", tagged.Samples, tagged.Ramp, Options);
        var runs = analysis.Metrics.Single(static m => string.Equals(m.Name, "series:Tracon/tracon.runs", StringComparison.Ordinal));
        var other = analysis.Metrics.Single(static m => string.Equals(m.Name, "series:Tracon/tracon.agent.source_failures", StringComparison.Ordinal));

        runs.Kind.ShouldBe(MetricKind.Objects);
        runs.Label.ShouldContain("one per tenant by design");
        runs.Verdict.ShouldBe(FootprintVerdict.Green, string.Join("; ", runs.Reasons));
        runs.Steps.Last().PerTenant.ShouldBe(3.0, 0.05);

        other.Kind.ShouldBe(MetricKind.Count);
        other.Verdict.ShouldBe(FootprintVerdict.Red);

        // The same readings from a host that did not add the tag are what the rule for a count was written for.
        var untagged = Fixture.Build((_, _, _, _) => null);
        untagged.Samples.ForEach(sample => Series(sample, tenants => 3L * tenants));

        FootprintAnalysis.Analyse("cell", untagged.Samples, untagged.Ramp, Options)
            .Metrics.Single(static m => string.Equals(m.Name, "series:Tracon/tracon.runs", StringComparison.Ordinal))
            .Verdict.ShouldBe(FootprintVerdict.Red);
    }

    [Fact]
    public void Only_the_eight_instruments_the_product_tags_are_judged_as_one_series_per_tenant()
    {
        string[] tagged =
        [
            "tracon.runs", "tracon.run.cost", "tracon.judge.cost", "tracon.judge.score", "tracon.model.cache",
            "tracon.job.executions", "tracon.audit.write_failures", "tracon.run.recording_failures",
        ];

        foreach (var instrument in tagged)
        {
            FootprintCatalog.IsTenantTaggedSeries("series:Tracon/" + instrument).ShouldBeTrue(instrument);
        }

        // The duration histograms are not tagged with the tenant: a series count that follows the tenants there is a leak.
        FootprintCatalog.IsTenantTaggedSeries("series:Tracon/tracon.run.duration").ShouldBeFalse();
        FootprintCatalog.IsTenantTaggedSeries("series:Tracon/tracon.job.duration").ShouldBeFalse();
        FootprintCatalog.IsTenantTaggedSeries("host.osThreads").ShouldBeFalse();
        FootprintCatalog.IsTenantTaggedSeries("series:Tracon/gen_ai.client.token.usage").ShouldBeFalse();
    }

    [Fact]
    public void A_series_for_every_tenant_whose_cost_per_tenant_climbs_is_red_even_when_the_tag_is_on()
    {
        // Three series each for the first tenants and more for each tenant after: super-linear in the tenants.
        var run = Fixture.Build((_, _, _, _) => null);
        run.Ramp.TenantTagged = true;
        run.Samples.ForEach(sample => sample.InstrumentSeries = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["Tracon/tracon.runs"] = 12 + (3L * sample.Tenants) + ((long)sample.Tenants * sample.Tenants / 10),
        });

        var runs = FootprintAnalysis.Analyse("cell", run.Samples, run.Ramp, Options)
            .Metrics.Single(static m => string.Equals(m.Name, "series:Tracon/tracon.runs", StringComparison.Ordinal));

        runs.Verdict.ShouldBe(FootprintVerdict.Red);
        runs.Reasons.ShouldContain(r => r.Contains("super-linear", StringComparison.Ordinal));
    }

    [Fact]
    public void A_series_that_appears_when_its_tenant_is_first_exercised_is_set_aside_inside_a_plateau_when_the_tag_is_on()
    {
        // The tenants are fixed and the series count still climbs through the plateau, because a tenant's series appears
        // when it first has an event: the tag explains that. The same climb with the tag off is a series made per run.
        static void Climb(FootprintSample sample)
            => sample.InstrumentSeries = new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["Tracon/tracon.runs"] = 12 + (3L * sample.Tenants) + (long)(sample.OffsetSeconds / 2),
            };

        var tagged = Fixture.Build((_, _, _, _) => null);
        tagged.Ramp.TenantTagged = true;
        tagged.Samples.ForEach(Climb);

        var runs = FootprintAnalysis.Analyse("cell", tagged.Samples, tagged.Ramp, Options)
            .Metrics.Single(static m => string.Equals(m.Name, "series:Tracon/tracon.runs", StringComparison.Ordinal));

        runs.Trends.ShouldContain(static trend => string.Equals(trend.Verdict, FootprintVerdict.Expected, StringComparison.Ordinal));
        runs.Trends.ShouldNotContain(static trend => string.Equals(trend.Verdict, FootprintVerdict.Red, StringComparison.Ordinal));
        runs.Notes.ShouldContain(static note => note.Contains("first has an event", StringComparison.Ordinal));
        string.Equals(runs.Verdict, FootprintVerdict.Red, StringComparison.Ordinal).ShouldBeFalse(string.Join("; ", runs.Reasons));

        var untagged = Fixture.Build((_, _, _, _) => null);
        untagged.Samples.ForEach(Climb);

        FootprintAnalysis.Analyse("cell", untagged.Samples, untagged.Ramp, Options)
            .Metrics.Single(static m => string.Equals(m.Name, "series:Tracon/tracon.runs", StringComparison.Ordinal))
            .Verdict.ShouldBe(FootprintVerdict.Red);
    }

    [Fact]
    public void The_total_of_every_series_follows_the_tenants_when_the_tag_is_on_and_is_a_leak_when_it_is_off()
    {
        static void Total(FootprintSample sample) => sample.Values["host.meterSeries"] = 110 + (2.0 * sample.Tenants);

        var tagged = Fixture.Build((_, _, _, _) => null);
        tagged.Ramp.TenantTagged = true;
        tagged.Samples.ForEach(Total);

        var total = FootprintAnalysis.Analyse("cell", tagged.Samples, tagged.Ramp, Options)
            .Metrics.Single(static m => string.Equals(m.Name, "host.meterSeries", StringComparison.Ordinal));

        total.Kind.ShouldBe(MetricKind.Objects);
        total.Verdict.ShouldBe(FootprintVerdict.Green, string.Join("; ", total.Reasons));

        var untagged = Fixture.Build((_, _, _, _) => null);
        untagged.Samples.ForEach(Total);

        FootprintAnalysis.Analyse("cell", untagged.Samples, untagged.Ramp, Options)
            .Metrics.Single(static m => string.Equals(m.Name, "host.meterSeries", StringComparison.Ordinal))
            .Verdict.ShouldBe(FootprintVerdict.Red);
    }

    [Fact]
    public void Only_the_connection_count_and_the_timers_are_judged_as_a_pool_and_the_ones_in_a_transaction_stay_plain()
    {
        FootprintCatalog.Find("pg.connections").ShouldNotBeNull().Kind.ShouldBe(MetricKind.Pooled);
        FootprintCatalog.Find("pg.connections").ShouldNotBeNull().HasPoolCeiling.ShouldBeTrue();
        FootprintCatalog.Find("host.activeTimers").ShouldNotBeNull().Kind.ShouldBe(MetricKind.Pooled);
        FootprintCatalog.Find("host.activeTimers").ShouldNotBeNull().HasPoolCeiling.ShouldBeFalse();
        FootprintCatalog.Find("pg.idleInTransaction").ShouldNotBeNull().Kind.ShouldBe(MetricKind.Count);
    }

    [Fact]
    public void Framework_pool_gauges_are_written_down_and_judged_by_nobody()
    {
        FootprintCatalog.Find("host.upDownTotals.Microsoft.AspNetCore.MemoryPool/aspnetcore.memory_pool.pooled").ShouldBeNull();
        FootprintCatalog.Find("host.meterRepeatedPublications.Tracon/gen_ai.client.token.usage").ShouldNotBeNull().Kind.ShouldBe(MetricKind.Objects);
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

    // A pool between 4 and 18 connections whose cycle is as long as a plateau. The phase of each stage puts the end of its
    // plateau at 9, 13 and 17, which the median of the whole plateau (about 11 in all three) does not follow.
    private static readonly double[] PoolPhases = [2.190, 3.107, 2.094];

    private static double PoolWave(double time, int stage)
        => 11 + (7 * Math.Sin((2 * Math.PI * time / 48) + PoolPhases[stage]));

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
                    "pg.connections", "host.liveHeapBytes", "host.meterPublications",
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
