using Tracon.CapacityDriver;

namespace Tracon.Capacity.Tests;

/// <summary>Write amplification arithmetic, including the bloat trap it must not fall into.</summary>
public sealed class CapacityStorageAccountingTests
{
    private static readonly Dictionary<string, StorageAccountant.TableReading> Empty = new(StringComparer.Ordinal);

    private static StorageAccountant.TableReading Reading(
        long rows, long heap, long index = 0, long toast = 0, long dead = 0, long vacuums = 0)
        => new(rows, heap, index, toast, dead, vacuums);

    [Fact]
    public void Growth_is_the_difference_and_is_split_into_heap_index_and_toast()
    {
        var before = new Dictionary<string, StorageAccountant.TableReading>(StringComparer.Ordinal)
        {
            ["runs"] = Reading(0, 1000, index: 500, toast: 100),
        };

        var after = new Dictionary<string, StorageAccountant.TableReading>(StringComparer.Ordinal)
        {
            ["runs"] = Reading(10, 3000, index: 1500, toast: 400),
        };

        var summary = StorageAccountant.Difference(before, after, completedRuns: 10, recordedEvents: 200, retentionEnabled: false);

        var runs = summary.Tables.Single(static t => string.Equals(t.Table, "runs", StringComparison.Ordinal));
        runs.Rows.ShouldBe(10);
        runs.HeapBytes.ShouldBe(2000);
        runs.IndexBytes.ShouldBe(1000);
        runs.ToastBytes.ShouldBe(300);
        runs.TotalBytes.ShouldBe(3300);

        summary.BytesPerRun.ShouldBe(330);
        summary.RowsPerRun.ShouldBe(1);
        summary.BytesPerEvent.ShouldBe(16.5);
    }

    [Fact]
    public void Autovacuum_inside_the_window_flags_the_cell_rather_than_silently_changing_the_number()
    {
        // 🚨 pg_total_relation_size counts bloat. The row count is unaffected,
        // which is exactly why rows and bytes are always reported together.
        var before = new Dictionary<string, StorageAccountant.TableReading>(StringComparer.Ordinal)
        {
            ["run_events"] = Reading(0, 1000, vacuums: 3),
        };

        var after = new Dictionary<string, StorageAccountant.TableReading>(StringComparer.Ordinal)
        {
            ["run_events"] = Reading(100, 900, vacuums: 4),
        };

        var summary = StorageAccountant.Difference(before, after, completedRuns: 5, recordedEvents: 100, retentionEnabled: false);

        summary.VacuumInterference.ShouldBeTrue();
        summary.Tables.Single().Vacuumed.ShouldBeTrue();

        // The row count survives the vacuum and stays usable.
        summary.Tables.Single().Rows.ShouldBe(100);
    }

    [Fact]
    public void A_table_absent_from_the_schema_is_omitted_rather_than_written_as_a_zero()
    {
        var after = new Dictionary<string, StorageAccountant.TableReading>(StringComparer.Ordinal)
        {
            ["runs"] = Reading(1, 100),
        };

        var summary = StorageAccountant.Difference(Empty, after, completedRuns: 1, recordedEvents: 1, retentionEnabled: false);

        summary.Tables.Count.ShouldBe(1);
        summary.Tables.ShouldAllBe(static t => string.Equals(t.Table, "runs", StringComparison.Ordinal));
    }

    [Fact]
    public void No_measured_table_means_unavailable_rather_than_zero_growth()
    {
        var summary = StorageAccountant.Difference(Empty, Empty, completedRuns: 5, recordedEvents: 50, retentionEnabled: false);

        summary.Available.ShouldBeFalse();
        summary.Unavailable.ShouldNotBeNull();
        summary.BytesPerRun.ShouldBeNull();
    }

    [Fact]
    public void Zero_completed_runs_produces_no_per_run_number_rather_than_a_division_by_zero()
    {
        var after = new Dictionary<string, StorageAccountant.TableReading>(StringComparer.Ordinal)
        {
            ["runs"] = Reading(0, 0),
        };

        var summary = StorageAccountant.Difference(Empty, after, completedRuns: 0, recordedEvents: 0, retentionEnabled: false);

        summary.BytesPerRun.ShouldBeNull();
        summary.RowsPerRun.ShouldBeNull();
        summary.BytesPerEvent.ShouldBeNull();
    }

    [Fact]
    public void Retention_state_travels_with_the_number_because_it_changes_the_question()
    {
        var summary = StorageAccountant.Difference(Empty, Empty, completedRuns: 0, recordedEvents: 0, retentionEnabled: true);

        summary.RetentionEnabled.ShouldBeTrue();
    }

    [Fact]
    public void Byte_formatting_says_no_measurement_rather_than_zero()
    {
        StorageAccountant.FormatBytes(null).ShouldBe("—");
        StorageAccountant.FormatBytes(0).ShouldBe("0 B");
        StorageAccountant.FormatBytes(2048).ShouldBe("2 KiB");
    }
}
