using Tracon.CapacityDriver;

namespace Tracon.Capacity.Tests;

/// <summary>What happens when a measurement could not be taken.</summary>
/// <remarks>
/// 🚨 The rule the whole report rests on: an unavailable instrument is never
/// written as a zero. A zero reads as "measured, and it was nothing", which is
/// a different claim and an unfalsifiable one.
/// </remarks>
public sealed class CapacityTelemetryTests
{
    [Fact]
    public void An_unavailable_measurement_carries_its_reason_and_no_value()
    {
        var coverage = new TelemetryCoverage();
        coverage.Unavailable["process.cpu"] = "processor time is not readable on this platform";

        coverage.Available.ShouldNotContain("process.cpu", StringComparer.Ordinal);
        coverage.Unavailable["process.cpu"].ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void A_missing_mandatory_measurement_is_listed_separately_from_an_optional_one()
    {
        var coverage = new TelemetryCoverage();
        coverage.Unavailable["process.gc"] = "only readable for the driver's own process";
        coverage.MissingMandatory.Add("http.latency");

        // Optional gaps narrow the coverage; mandatory gaps invalidate the cell.
        coverage.MissingMandatory.ShouldHaveSingleItem();
        coverage.MissingMandatory.ShouldContain("http.latency", StringComparer.Ordinal);
    }

    [Fact]
    public void A_cell_records_every_invalidation_reason_rather_than_only_the_first()
    {
        var cell = new CellResult();

        cell.Invalidate("first");
        cell.Invalidate("second");

        cell.Status.ShouldBe(CellStatus.Invalid);
        cell.StatusReasons.ShouldBe(["first", "second"]);
    }

    [Fact]
    public void An_incomplete_cell_names_why_it_stopped()
    {
        var cell = new CellResult();

        cell.Incomplete(CellStatus.DrainLimit);

        cell.Status.ShouldBe("incomplete/drain-limit");
        cell.StatusReasons.ShouldContain(CellStatus.DrainLimit, StringComparer.Ordinal);
    }

    [Fact]
    public void Invalid_outranks_incomplete_because_untrustworthy_is_worse_than_short()
    {
        var cell = new CellResult();

        cell.Invalidate("tenant bleed");
        cell.Incomplete(CellStatus.ResourceLimit);

        cell.Status.ShouldBe(CellStatus.Invalid);
        cell.StatusReasons.Count.ShouldBe(2);
    }

    [Fact]
    public void A_slow_cell_is_not_invalid()
    {
        // 🚨 Slowness is a finding about the system, never about the
        // measurement. Only a measurement that cannot be trusted is invalid.
        var cell = new CellResult { ThroughputPerSecond = 0.01 };

        cell.Status.ShouldBe(CellStatus.Complete);
    }
}

/// <summary>Which measurements count as present, and where each one may live.</summary>
public sealed class TelemetryCoverageBuilderTests
{
    private static HostTelemetry Host(long turns = 10, long recordingFailures = 0)
        => new(ModelCalls: 5, ModelTurns: turns, ToolInvocations: 5, RecordingFailures: recordingFailures, Unavailable: []);

    private static CellResult Cell(long modelTurns, bool queued = false)
    {
        var cell = new CellResult
        {
            Scenarios = queued ? [CapacityScenario.Queued] : [CapacityScenario.Buffered],
            Model = new ModelSummary { Turns = modelTurns },
            Processes = [new ProcessResourceSummary { Role = "host", PeakRssBytes = 1, ProcessorSeconds = 1 }],
            Storage = new StorageSummary { Available = true },
            QueueWait = queued ? new LatencySummary { Count = 5 } : new LatencySummary(),
        };

        cell.LatencyByScenario[cell.Scenarios[0]] = new LatencySummary { Count = 100 };
        return cell;
    }

    [Fact]
    public void Model_turns_recorded_only_by_the_worker_processes_still_count()
    {
        // 🚨 The defect this case exists for: on the worker axis the HTTP host
        // serves no model call, so its counter is legitimately zero while the
        // turns sit - correctly recorded - in the execution log the whole
        // worker distribution is built from. Requiring the host counter marked
        // every worker-axis cell invalid over a measurement that was present.
        var coverage = TelemetryCoverageBuilder.Build(Cell(modelTurns: 368), Host(turns: 0), []);

        coverage.Available.ShouldContain("model.turns", StringComparer.Ordinal);
        coverage.MissingMandatory.ShouldBeEmpty();
    }

    [Fact]
    public void Model_turns_recorded_only_by_the_host_still_count()
    {
        var coverage = TelemetryCoverageBuilder.Build(Cell(modelTurns: 0), Host(turns: 12), []);

        coverage.Available.ShouldContain("model.turns", StringComparer.Ordinal);
        coverage.MissingMandatory.ShouldBeEmpty();
    }

    [Fact]
    public void Neither_source_recording_a_turn_is_still_a_missing_mandatory_measurement()
    {
        var coverage = TelemetryCoverageBuilder.Build(Cell(modelTurns: 0), Host(turns: 0), []);

        coverage.MissingMandatory.ShouldContain("model.turns", StringComparer.Ordinal);
        coverage.Unavailable["model.turns"].ShouldContain("neither");
    }

    [Fact]
    public void Queue_wait_is_mandatory_only_where_something_was_queued()
    {
        TelemetryCoverageBuilder.Build(Cell(modelTurns: 5), Host(), []).MissingMandatory.ShouldBeEmpty();

        var queuedWithoutWait = Cell(modelTurns: 5, queued: true);
        queuedWithoutWait.QueueWait = new LatencySummary();

        TelemetryCoverageBuilder.Build(queuedWithoutWait, Host(), [])
            .MissingMandatory.ShouldContain("queue.wait", StringComparer.Ordinal);
    }

    [Fact]
    public void A_process_that_could_not_be_sampled_is_named_rather_than_ignored()
    {
        var coverage = TelemetryCoverageBuilder.Build(Cell(modelTurns: 5), Host(), ["worker-2"]);

        coverage.Unavailable.ShouldContainKey("process.worker-2");
    }
}
