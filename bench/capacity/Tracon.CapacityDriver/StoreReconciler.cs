using Npgsql;

namespace Tracon.CapacityDriver;

/// <summary>Checks what the store holds against what the driver sent.</summary>
/// <remarks>
/// <para>
/// 🚨 A recording failure does not stop a run: Tracon's own rule is that
/// observability must not break functionality, so a store that refuses a write
/// leaves the run itself green. That is correct product behaviour and it is
/// precisely why a capacity run that only counted HTTP successes could report a
/// clean measurement over lost data. Every cell therefore reconciles.
/// </para>
/// <para>
/// The queries are read-only aggregates over the cell's own schema. Tenant
/// isolation is checked here as well: a row of one tenant visible under the
/// other is not a slow cell, it is an invalid one.
/// </para>
/// </remarks>
public sealed class StoreReconciler
{
    private readonly string _connectionString;
    private readonly string _schema;

    /// <summary>Creates a reconciler over one schema.</summary>
    /// <param name="connectionString">How to reach the database, read-only.</param>
    /// <param name="schema">The schema the cell owns.</param>
    public StoreReconciler(string connectionString, string schema)
    {
        _connectionString = connectionString;
        _schema = schema;
    }

    /// <summary>What the store says about one cohort of runs.</summary>
    /// <param name="Terminal">How many reached a terminal status.</param>
    /// <param name="Missing">How many accepted ids the store does not hold.</param>
    /// <param name="Events">How many events were recorded for them.</param>
    /// <param name="TenantBleed">How many of them sit under the wrong tenant.</param>
    /// <param name="QueueWaitMilliseconds">Per-job wait between creation and the first attempt.</param>
    public readonly record struct RunCohort(
        long Terminal,
        long Missing,
        long Events,
        long TenantBleed,
        IReadOnlyList<double> QueueWaitMilliseconds);

    /// <summary>Reconciles a cohort of accepted run ids against the store.</summary>
    /// <param name="runIdsByTenant">The accepted run ids, grouped by the tenant that created them.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>What the store says.</returns>
    public async Task<RunCohort> ReconcileAsync(
        IReadOnlyDictionary<string, IReadOnlyList<Guid>> runIdsByTenant,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runIdsByTenant);

        var all = runIdsByTenant.SelectMany(static pair => pair.Value).ToArray();

        if (all.Length == 0)
        {
            return new RunCohort(0, 0, 0, 0, []);
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var terminal = await ScalarAsync(
            connection,
            $"SELECT COUNT(*) FROM {Qualified("runs")} WHERE id = ANY(@ids) AND status IN (1, 2, 3)",
            cancellationToken,
            ("ids", all)).ConfigureAwait(false);

        var present = await ScalarAsync(
            connection,
            $"SELECT COUNT(*) FROM {Qualified("runs")} WHERE id = ANY(@ids)",
            cancellationToken,
            ("ids", all)).ConfigureAwait(false);

        var events = await ScalarAsync(
            connection,
            $"SELECT COUNT(*) FROM {Qualified("run_events")} WHERE run_id = ANY(@ids)",
            cancellationToken,
            ("ids", all)).ConfigureAwait(false);

        long bleed = 0;

        foreach (var (tenant, ids) in runIdsByTenant)
        {
            if (ids.Count == 0)
            {
                continue;
            }

            bleed += await ScalarAsync(
                connection,
                $"SELECT COUNT(*) FROM {Qualified("runs")} WHERE id = ANY(@ids) AND tenant_id IS DISTINCT FROM @tenant",
                cancellationToken,
                ("ids", ids.ToArray()),
                ("tenant", tenant)).ConfigureAwait(false);
        }

        var waits = new List<double>();

        await using (var command = connection.CreateCommand())
        {
            // The job's own timestamps, not the client's: this is the wait
            // between the queue accepting the work and a worker starting it.
            command.CommandText =
                $"SELECT EXTRACT(EPOCH FROM (started_at - created_at)) * 1000 " +
                $"FROM {Qualified("jobs")} WHERE id = ANY(@ids) AND started_at IS NOT NULL";
            command.Parameters.AddWithValue("ids", all);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                waits.Add((double)reader.GetDecimal(0));
            }
        }

        return new RunCohort(terminal, all.Length - present, events, bleed, waits);
    }

    /// <summary>How many rows a table holds, for the seed verification.</summary>
    /// <param name="table">The table to count.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The row count.</returns>
    public async Task<long> CountAsync(string table, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ScalarAsync(connection, $"SELECT COUNT(*) FROM {Qualified(table)}", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>How many recorded event streams have a gap in their sequence.</summary>
    /// <param name="runIds">The runs to inspect.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>How many streams are not contiguous.</returns>
    public async Task<long> SequenceGapsAsync(IReadOnlyList<Guid> runIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runIds);

        if (runIds.Count == 0)
        {
            return 0;
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // A contiguous stream has as many rows as the span between its first
        // and last sequence number. Anything else lost or skipped a row.
        return await ScalarAsync(
            connection,
            $"""
             SELECT COUNT(*) FROM (
                 SELECT run_id, COUNT(*) AS event_rows, MIN(seq) AS lo, MAX(seq) AS hi
                 FROM {Qualified("run_events")}
                 WHERE run_id = ANY(@ids)
                 GROUP BY run_id
             ) s WHERE s.event_rows <> s.hi - s.lo + 1
             """,
            cancellationToken,
            ("ids", runIds.ToArray())).ConfigureAwait(false);
    }

    private string Qualified(string table)
        => $"\"{_schema.Replace("\"", "\"\"", StringComparison.Ordinal)}\".\"{table}\"";

    private static async Task<long> ScalarAsync(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is long value2 ? value2 : Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }
}
