using System.Text.Json;
using Tracon.CapacityDriver;

namespace Tracon.Capacity.Tests;

/// <summary>The merge from cell files on disk into one summary, one report and its charts.</summary>
public sealed class CapacityReportTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "capacity-report-" + Guid.NewGuid().ToString("N"));

    private void WriteCell(CellResult cell)
    {
        var path = Path.Combine(_directory, "cells", cell.CellId);
        Directory.CreateDirectory(path);
        File.WriteAllText(
            Path.Combine(path, "cell.json"),
            JsonSerializer.Serialize(cell, CapacityJson.Default.CellResult));
    }

    private static CellResult Cell(string id, int concurrency, int repeat, double throughput, string status = CellStatus.Complete)
        => new()
        {
            RunId = "run",
            CellId = id,
            Profile = "sweep",
            Scenarios = [CapacityScenario.Buffered],
            SeedShape = "empty",
            Concurrency = concurrency,
            Repeat = repeat,
            Status = status,
            MeasuredSeconds = 60,
            ThroughputPerSecond = throughput,
            LatencyByScenario = new Dictionary<string, LatencySummary>(StringComparer.Ordinal)
            {
                [CapacityScenario.Buffered] = new() { Count = 120, P50 = 100, P95 = 200, P99 = 300 },
            },
        };

    [Fact]
    public void Repeats_of_one_load_point_become_one_row_with_their_spread()
    {
        WriteCell(Cell("a", concurrency: 8, repeat: 1, throughput: 10));
        WriteCell(Cell("b", concurrency: 8, repeat: 2, throughput: 12));
        WriteCell(Cell("c", concurrency: 8, repeat: 3, throughput: 14));

        var summary = ReportBuilder.Build(_directory);

        summary.Cells.ShouldBe(3);
        summary.CompleteCells.ShouldBe(3);

        var row = summary.Rows.Single();
        row.Repeats.ShouldBe(3);
        row.ThroughputPerSecond.ShouldBe(12);
        row.ThroughputSpread.ShouldBe(4);

        // The sample count is the SUM across repeats, which is what the low
        // sample warnings depend on.
        row.Latency.Count.ShouldBe(360);
        row.Latency.LowSampleP95.ShouldBeFalse();
        row.Latency.LowSampleP99.ShouldBeTrue();
    }

    [Fact]
    public void An_invalid_cell_is_counted_and_kept_out_of_the_rows()
    {
        WriteCell(Cell("a", 8, 1, 10));
        var invalid = Cell("b", 8, 2, 999, CellStatus.Invalid);
        invalid.StatusReasons.Add("2 accepted run id(s) are not in the store");
        WriteCell(invalid);

        var summary = ReportBuilder.Build(_directory);

        summary.InvalidCells.ShouldBe(1);
        summary.Rows.Single().Repeats.ShouldBe(1);
        summary.Caveats.ShouldContain(c => c.Contains("not in the store", StringComparison.Ordinal));
    }

    [Fact]
    public void An_incomplete_cell_is_named_in_the_caveats_rather_than_shown_as_finished()
    {
        var incomplete = Cell("a", 64, 1, 3);
        incomplete.Incomplete(CellStatus.ResourceLimit);
        WriteCell(incomplete);

        var summary = ReportBuilder.Build(_directory);

        summary.IncompleteCells.ShouldBe(1);
        summary.Caveats.ShouldContain(c => c.Contains("resource-limit", StringComparison.Ordinal));
    }

    [Fact]
    public void The_report_always_carries_the_not_an_SLA_frame_and_the_single_machine_limit()
    {
        WriteCell(Cell("a", 1, 1, 1));

        ReportBuilder.Build(_directory);

        var markdown = File.ReadAllText(Path.Combine(_directory, "report.md"));

        markdown.ShouldContain("not an SLA");
        markdown.ShouldContain("multi-node");
        markdown.ShouldContain("What this report does not say");
    }

    [Fact]
    public void Charts_are_standalone_svg_with_no_external_reference()
    {
        WriteCell(Cell("a", 1, 1, 1));

        ReportBuilder.Build(_directory);

        var svg = File.ReadAllText(Path.Combine(_directory, "charts", "load-latency.svg"));

        svg.ShouldStartWith("<svg");
        svg.ShouldNotContain("<script");
        svg.ShouldNotContain("http://www.w3.org/1999/xlink");
        svg.ShouldNotContain("@import");
    }

    [Fact]
    public void A_run_with_no_cell_says_so_instead_of_claiming_no_bottleneck()
    {
        Directory.CreateDirectory(_directory);

        var summary = ReportBuilder.Build(_directory);

        summary.Cells.ShouldBe(0);
        summary.Bottleneck.ShouldContain("no cell");
    }

    [Fact]
    public void No_saturation_is_reported_as_no_ceiling_in_range_rather_than_as_a_capacity()
    {
        WriteCell(Cell("a", 8, 1, 10));

        ReportBuilder.Build(_directory).Bottleneck.ShouldBe("no ceiling was found inside the measured range");
    }

    [Fact]
    public void A_driver_side_limit_is_not_reported_as_server_capacity()
    {
        var cell = Cell("a", 64, 1, 5);
        cell.Arrival.NotSent = 12;
        WriteCell(cell);

        ReportBuilder.Build(_directory).Bottleneck.ShouldContain("not reported as server capacity");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
