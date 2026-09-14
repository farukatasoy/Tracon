using Tracon.CapacityDriver;

namespace Tracon.Capacity.Tests;

/// <summary>The spec and limit validation that keeps an unmeasurable cell from looking like a fast one.</summary>
public sealed class CapacityBudgetTests
{
    private static CellSpec Valid() => new()
    {
        RunId = "run",
        CellId = "cell",
        Profile = "smoke",
        Scenarios = [CapacityScenario.Buffered],
        Tenants = ["a", "b"],
        Concurrency = 2,
        Repeat = 1,
        RunsPerTenant = 2,
        BaseAddress = "http://127.0.0.1:5199/tracon",
        Schema = "capacity",
        OutputDirectory = "/tmp/capacity",
    };

    [Fact]
    public void A_usable_spec_produces_no_problem()
        => Valid().Validate().ShouldBeEmpty();

    [Fact]
    public void A_cell_with_no_bound_is_refused()
    {
        var spec = Valid();
        spec.RunsPerTenant = 0;
        spec.MeasureSeconds = 0;

        spec.Validate().ShouldContain(p => p.Contains("a cell needs a bound", StringComparison.Ordinal));
    }

    [Fact]
    public void Two_bounds_at_once_are_refused_because_only_one_can_be_the_denominator()
    {
        var spec = Valid();
        spec.MeasureSeconds = 60;

        spec.Validate().ShouldContain(p => p.Contains("two different bounds", StringComparison.Ordinal));
    }

    [Fact]
    public void A_negative_duration_is_refused()
    {
        var spec = Valid();
        spec.WarmupSeconds = -1;

        spec.Validate().ShouldContain(p => p.Contains("warmupSeconds", StringComparison.Ordinal));
    }

    [Fact]
    public void Zero_concurrency_is_refused_rather_than_producing_an_empty_fast_cell()
    {
        var spec = Valid();
        spec.Concurrency = 0;

        spec.Validate().ShouldContain(p => p.Contains("concurrency", StringComparison.Ordinal));
    }

    [Fact]
    public void Concurrency_above_the_in_flight_cap_is_refused()
    {
        var spec = Valid();
        spec.Concurrency = 500;

        spec.Validate().ShouldContain(p => p.Contains("exceeds the in-flight cap", StringComparison.Ordinal));
    }

    [Fact]
    public void Exactly_two_tenants_are_required_because_the_reconciliation_compares_one_against_the_other()
    {
        var spec = Valid();
        spec.Tenants = ["only-one"];

        spec.Validate().ShouldContain(p => p.Contains("exactly two tenants", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unknown_scenario_is_refused()
    {
        var spec = Valid();
        spec.Scenarios = ["websocket"];

        spec.Validate().ShouldContain(p => p.Contains("unknown scenario", StringComparison.Ordinal));
    }

    [Fact]
    public void The_arrival_shape_is_only_defined_for_the_queued_scenario()
    {
        var spec = Valid();
        spec.ArrivalRatePerSecond = 4;

        spec.Validate().ShouldContain(p => p.Contains("only defined for the queued scenario", StringComparison.Ordinal));
    }

    [Fact]
    public void The_worker_axis_refuses_a_pid_count_that_does_not_match_its_label()
    {
        // 🚨 The whole axis shifts by one if the number on it is not the number
        // of processes that actually leased.
        var spec = Valid();
        spec.Scenarios = [CapacityScenario.Queued];
        spec.WorkerCount = 4;
        spec.WorkerPids = [111, 222];

        spec.Validate().ShouldContain(p => p.Contains("2 worker pids were supplied", StringComparison.Ordinal));
    }

    [Fact]
    public void A_zero_or_negative_limit_is_refused()
    {
        var spec = Valid();
        spec.Limits.MaxInFlight = 0;
        spec.Limits.HostRssBytes = -1;

        var problems = spec.Validate();

        problems.ShouldContain(p => p.Contains("limits.maxInFlight", StringComparison.Ordinal));
        problems.ShouldContain(p => p.Contains("limits.hostRssBytes", StringComparison.Ordinal));
    }

    [Fact]
    public void A_workload_that_produces_no_output_is_refused()
    {
        var spec = Valid();
        spec.Workload.OutputChunks = 0;

        spec.Validate().ShouldContain(p => p.Contains("workload.outputChunks", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_bounded_writer_stops_appending_at_its_cap_and_reports_what_it_dropped()
    {
        var path = Path.Combine(Path.GetTempPath(), "capacity-bounded-" + Guid.NewGuid().ToString("N"), "rows.jsonl");

        await using (var writer = new BoundedJsonlWriter<RequestSample>(path, CapacityJson.Default.RequestSample, maximumRows: 3))
        {
            for (var index = 0; index < 10; index++)
            {
                await writer.WriteAsync(new RequestSample { CellId = "cell", Correlation = index.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            }

            writer.Written.ShouldBe(3);
            writer.Dropped.ShouldBe(7);
        }

        File.ReadAllLines(path).Length.ShouldBe(3);
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }

    [Fact]
    public void A_bounded_writer_refuses_a_cap_of_zero()
    {
        var path = Path.Combine(Path.GetTempPath(), "capacity-" + Guid.NewGuid().ToString("N"), "rows.jsonl");

        Should.Throw<ArgumentOutOfRangeException>(
            () => new BoundedJsonlWriter<RequestSample>(path, CapacityJson.Default.RequestSample, maximumRows: 0));
    }
}
