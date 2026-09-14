using System.Globalization;
using Npgsql;

namespace Tracon.CapacityDriver;

/// <summary>Measures what one cell's schema grew by, table by table.</summary>
/// <remarks>
/// <para>
/// 🚨 <c>pg_total_relation_size</c> counts <strong>bloat</strong> as well as
/// live data, so the same workload can produce different byte numbers when
/// autovacuum happens to run inside the window. Three defences are applied and
/// all three are reported: the cell owns a fresh schema, the autovacuum state
/// is read on both sides of the window, and the closing reading is taken only
/// after the drain has finished.
/// </para>
/// <para>
/// Rows and bytes are always reported together. Row counts are immune to bloat;
/// when the two diverge, that divergence <em>is</em> the bloat signal, and a
/// report that showed only one of them would hide it.
/// </para>
/// </remarks>
public sealed class StorageAccountant
{
    /// <summary>The tables whose growth a run is accounted against.</summary>
    /// <remarks>
    /// A single "N KB per run" number is not enough: which table grew changes
    /// the capacity decision, because retention, indexing and payload size are
    /// different levers on different tables.
    /// </remarks>
    public static readonly string[] MeasuredTables =
    [
        "runs",
        "run_events",
        "run_inputs",
        "tool_invocations",
        "jobs",
        "job_items",
        "traces",
        "spans",
        "idempotency_keys",
        "sessions",
    ];

    private readonly string _connectionString;
    private readonly string _schema;

    /// <summary>Creates an accountant over one schema.</summary>
    /// <param name="connectionString">How to reach the database, read-only.</param>
    /// <param name="schema">The schema the cell owns.</param>
    public StorageAccountant(string connectionString, string schema)
    {
        _connectionString = connectionString;
        _schema = schema;
    }

    /// <summary>One table's absolute size and row count at a moment in time.</summary>
    /// <param name="Rows">Live rows.</param>
    /// <param name="HeapBytes">Bytes in the table's own heap.</param>
    /// <param name="IndexBytes">Bytes in its indexes.</param>
    /// <param name="ToastBytes">Bytes in its TOAST storage.</param>
    /// <param name="DeadTuples">Dead tuples as the statistics collector sees them.</param>
    /// <param name="AutovacuumCount">How many times autovacuum has run on the table.</param>
    public readonly record struct TableReading(
        long Rows,
        long HeapBytes,
        long IndexBytes,
        long ToastBytes,
        long DeadTuples,
        long AutovacuumCount);

    /// <summary>Reads every measured table's current size.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One reading per table that exists.</returns>
    public async Task<Dictionary<string, TableReading>> ReadAsync(CancellationToken cancellationToken)
    {
        var readings = new Dictionary<string, TableReading>(StringComparer.Ordinal);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        foreach (var table in MeasuredTables)
        {
            if (await ReadTableAsync(connection, table, cancellationToken).ConfigureAwait(false) is { } reading)
            {
                readings[table] = reading;
            }
        }

        return readings;
    }

    private async Task<TableReading?> ReadTableAsync(
        NpgsqlConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        var qualified = $"{Quote(_schema)}.{Quote(table)}";

        await using var command = connection.CreateCommand();

        // The row count is a real COUNT(*), not a planner estimate: the whole
        // point of the row column is to be the number bloat cannot move.
        command.CommandText = $"""
            SELECT
                (SELECT COUNT(*) FROM {qualified}),
                pg_table_size(c.oid) - COALESCE(pg_total_relation_size(c.reltoastrelid), 0),
                pg_indexes_size(c.oid),
                COALESCE(pg_total_relation_size(c.reltoastrelid), 0),
                COALESCE(s.n_dead_tup, 0),
                COALESCE(s.autovacuum_count, 0)
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            LEFT JOIN pg_stat_all_tables s ON s.relid = c.oid
            WHERE n.nspname = @schema AND c.relname = @table
            """;

        command.Parameters.AddWithValue("schema", _schema);
        command.Parameters.AddWithValue("table", table);

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return new TableReading(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetInt64(5));
        }
        catch (PostgresException ex) when (string.Equals(ex.SqlState, "42P01", StringComparison.Ordinal))
        {
            // The table is not in this schema - an optional migration set was
            // not applied. That is not an error and is not written as a zero;
            // the table simply does not appear in the breakdown.
            return null;
        }
    }

    /// <summary>The whole schema's total size, used against the database cap.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The size in bytes.</returns>
    public async Task<long> TotalBytesAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(SUM(pg_total_relation_size(c.oid)), 0)::bigint
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = @schema AND c.relkind IN ('r', 'm')
            """;
        command.Parameters.AddWithValue("schema", _schema);

        return (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);
    }

    /// <summary>Turns two readings into the growth the report publishes.</summary>
    /// <param name="before">The reading taken before the window opened.</param>
    /// <param name="after">The reading taken after the drain finished.</param>
    /// <param name="completedRuns">Runs that completed inside the window.</param>
    /// <param name="recordedEvents">Events recorded inside the window.</param>
    /// <param name="retentionEnabled">Whether retention was deleting rows while the cell ran.</param>
    /// <returns>The summary.</returns>
    public static StorageSummary Difference(
        IReadOnlyDictionary<string, TableReading> before,
        IReadOnlyDictionary<string, TableReading> after,
        long completedRuns,
        long recordedEvents,
        bool retentionEnabled)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var summary = new StorageSummary
        {
            Available = after.Count > 0,
            CompletedRuns = completedRuns,
            RecordedEvents = recordedEvents,
            RetentionEnabled = retentionEnabled,
        };

        if (after.Count == 0)
        {
            summary.Unavailable = "no measured table was present in the schema";
            return summary;
        }

        long totalBytes = 0;
        long totalRows = 0;

        foreach (var table in MeasuredTables)
        {
            if (!after.TryGetValue(table, out var end))
            {
                continue;
            }

            before.TryGetValue(table, out var start);

            var growth = new TableGrowth
            {
                Table = table,
                Rows = end.Rows - start.Rows,
                HeapBytes = end.HeapBytes - start.HeapBytes,
                IndexBytes = end.IndexBytes - start.IndexBytes,
                ToastBytes = end.ToastBytes - start.ToastBytes,
                DeadTuples = end.DeadTuples,
                Vacuumed = end.AutovacuumCount > start.AutovacuumCount,
            };

            growth.TotalBytes = growth.HeapBytes + growth.IndexBytes + growth.ToastBytes;

            if (growth.Vacuumed)
            {
                summary.VacuumInterference = true;
            }

            totalBytes += growth.TotalBytes;
            totalRows += growth.Rows;
            summary.Tables.Add(growth);
        }

        summary.BytesPerRun = completedRuns > 0 ? Math.Round((double)totalBytes / completedRuns, 1) : null;
        summary.RowsPerRun = completedRuns > 0 ? Math.Round((double)totalRows / completedRuns, 2) : null;
        summary.BytesPerEvent = recordedEvents > 0 ? Math.Round((double)totalBytes / recordedEvents, 1) : null;

        return summary;
    }

    private static string Quote(string identifier)
        => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    /// <summary>A byte count rendered for a report table.</summary>
    /// <param name="bytes">The count.</param>
    /// <returns>The rendering.</returns>
    public static string FormatBytes(double? bytes)
    {
        if (bytes is null)
        {
            return "—";
        }

        var value = bytes.Value;
        string[] units = ["B", "KiB", "MiB", "GiB"];
        var unit = 0;

        while (Math.Abs(value) >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return value.ToString("0.##", CultureInfo.InvariantCulture) + " " + units[unit];
    }
}
