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
