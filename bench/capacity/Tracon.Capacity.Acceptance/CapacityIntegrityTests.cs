using Npgsql;
using Tracon.CapacityDriver;

namespace Tracon.Capacity.Acceptance;

/// <summary>The reconciliation must REJECT data loss, not report around it.</summary>
/// <remarks>
/// 🚨 Tracon's own rule is that observability must not break functionality: a
/// store that refuses a write leaves the run green. That is correct, and it is
/// precisely why a capacity run that only counted HTTP successes could report a
/// clean measurement over lost data. These cases plant the loss and prove the
/// reconciliation sees it.
/// </remarks>
public sealed class CapacityIntegrityTests
{
    private static StoreReconciler Reconciler()
        => new(AcceptanceEnvironment.ConnectionString, AcceptanceEnvironment.Schema);

    private static async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(AcceptanceEnvironment.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> PlantRunAsync(string tenant, int events)
    {
        var runId = Guid.NewGuid();

        await ExecuteAsync(
            $"""
             INSERT INTO {CapacityPathTests.Qualified("runs")}
                 (id, tenant_id, agent_name, status, started_at, completed_at, is_streaming, event_count)
             VALUES (@id, @tenant, 'integrity-fixture', 1, now(), now(), false, @count)
             """,
            ("id", runId), ("tenant", tenant), ("count", (long)events));

        for (var sequence = 0; sequence < events; sequence++)
        {
            await ExecuteAsync(
                $"""
                 INSERT INTO {CapacityPathTests.Qualified("run_events")}
                     (run_id, seq, type, text, created_at)
                 VALUES (@id, @seq, 1, 'x', now())
                 """,
                ("id", runId), ("seq", (long)sequence));
        }

        return runId;
    }

    private static async Task CleanAsync(Guid runId)
        => await ExecuteAsync($"DELETE FROM {CapacityPathTests.Qualified("runs")} WHERE id = @id", ("id", runId));

    [Fact]
    public async Task An_accepted_run_that_is_not_in_the_store_is_counted_as_missing()
    {
        var absent = Guid.NewGuid();

        var cohort = await Reconciler().ReconcileAsync(
            new Dictionary<string, IReadOnlyList<Guid>>(StringComparer.Ordinal) { ["t"] = [absent] },
            CancellationToken.None);

        cohort.Missing.ShouldBe(1);
        cohort.Terminal.ShouldBe(0);
    }

    [Fact]
    public async Task A_gap_in_a_recorded_stream_is_found()
    {
        var runId = await PlantRunAsync("integrity-a", events: 5);

        try
        {
            (await Reconciler().SequenceGapsAsync([runId], CancellationToken.None)).ShouldBe(0);

            // Lose one event in the middle - exactly what a failed recording
            // write leaves behind.
            await ExecuteAsync(
                $"DELETE FROM {CapacityPathTests.Qualified("run_events")} WHERE run_id = @id AND seq = 2",
                ("id", runId));

            (await Reconciler().SequenceGapsAsync([runId], CancellationToken.None)).ShouldBe(1);
        }
        finally
        {
            await CleanAsync(runId);
        }
    }

    [Fact]
    public async Task A_run_recorded_under_the_wrong_tenant_is_found()
    {
        var runId = await PlantRunAsync("integrity-wrong", events: 1);

        try
        {
            var cohort = await Reconciler().ReconcileAsync(
                new Dictionary<string, IReadOnlyList<Guid>>(StringComparer.Ordinal) { ["integrity-right"] = [runId] },
                CancellationToken.None);

            cohort.TenantBleed.ShouldBe(1);
        }
        finally
        {
            await CleanAsync(runId);
        }
    }

    [Fact]
    public async Task A_healthy_cohort_reconciles_clean()
    {
        var runId = await PlantRunAsync("integrity-ok", events: 3);

        try
        {
            var cohort = await Reconciler().ReconcileAsync(
                new Dictionary<string, IReadOnlyList<Guid>>(StringComparer.Ordinal) { ["integrity-ok"] = [runId] },
                CancellationToken.None);

            cohort.Terminal.ShouldBe(1);
            cohort.Missing.ShouldBe(0);
            cohort.Events.ShouldBe(3);
            cohort.TenantBleed.ShouldBe(0);
        }
        finally
        {
            await CleanAsync(runId);
        }
    }
}
