using Tracon.Tests.Workflows;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// The cancellation sweep of the workflow runner over the real SQLite run, checkpoint, request and response stores.
/// </summary>
/// <remarks>
/// <para>
/// The in-memory sweep (<see cref="WorkflowCancellationSweepTests"/>) stands in for a SQL store with a proxy that throws
/// when its token is already canceled. This class runs the same sweep where that is the store's own behavior, and adds
/// what only a database can leak: a connection that is never closed and a lock that is never released.
/// </para>
/// <para>
/// A SQL store throws on a canceled token by itself, so the two kinds of store the in-memory sweep tells apart are one
/// here: every combination of graph, start and way to cancel runs once, with a plan that adds nothing to the store.
/// </para>
/// </remarks>
public sealed class WorkflowCancellationSweepSqliteTests
{
    /// <summary>Gets every combination of graph, start and way to cancel.</summary>
    /// <returns>The combinations.</returns>
    public static TheoryData<SweepGraph, SweepStart, bool> Combinations()
    {
        var data = new TheoryData<SweepGraph, SweepStart, bool>();

        foreach (var graph in Enum.GetValues<SweepGraph>())
        {
            foreach (var start in Enum.GetValues<SweepStart>())
            {
                if (start == SweepStart.Resume && graph is not (SweepGraph.AwaitingInput or SweepGraph.ToolApproval))
                {
                    continue;
                }

                foreach (var operatorCancel in new[] { false, true })
                {
                    data.Add(graph, start, operatorCancel);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task A_workflow_run_cancelled_at_any_await_point_ends_closed_or_waiting_and_leaks_nothing_in_sqlite(
        SweepGraph graph,
        SweepStart start,
        bool operatorCancel)
    {
        await using var backend = await SqliteSweepBackend.CreateAsync();

        var report = await WorkflowCancellationSweep.SweepAsync(backend, graph, start, operatorCancel, storeHonorsToken: false);

        TestContext.Current.SendDiagnosticMessage($"SQLite {graph} {start}: {report.AwaitPoints} await points ({string.Join(", ", report.Endings.Select(static ending => $"{ending.Key} {ending.Value}"))}), {report.Elapsed.TotalSeconds:0.0} s (operator cancel: {operatorCancel})");

        report.Problems.ShouldBeEmpty();
    }

    /// <summary>
    /// The check for a leaked connection has to see one. A connection a store opened and never closed is what a run that
    /// was cancelled in the middle of a read would leave behind.
    /// </summary>
    [Fact]
    public async Task A_connection_that_is_never_closed_is_reported_as_a_leak()
    {
        await using var backend = await SqliteSweepBackend.CreateAsync();

        var connection = await backend.DataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        (await backend.FindLeaksAsync(TimeSpan.FromMilliseconds(200))).ShouldContain(static leak => leak.Contains("connection", StringComparison.Ordinal));

        await connection.DisposeAsync();

        (await backend.FindLeaksAsync(TimeSpan.FromSeconds(3))).ShouldBeEmpty();
    }

    /// <summary>
    /// The check for a lock has to see one. A write transaction nobody ended is what a run that was cancelled between its
    /// begin and its commit would leave behind.
    /// </summary>
    [Fact]
    public async Task A_write_lock_that_is_never_released_is_reported_as_a_leak()
    {
        await using var backend = await SqliteSweepBackend.CreateAsync();

        await using (var holder = backend.OpenConnectionOutsideTheStores())
        {
            await holder.OpenAsync(TestContext.Current.CancellationToken);

            await using var begin = holder.CreateCommand();
            begin.CommandText = "BEGIN IMMEDIATE;";
            await begin.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

            (await backend.FindLeaksAsync(TimeSpan.FromMilliseconds(200))).ShouldContain(static leak => leak.Contains("locked", StringComparison.Ordinal));

            await using var rollback = holder.CreateCommand();
            rollback.CommandText = "ROLLBACK;";
            await rollback.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        (await backend.FindLeaksAsync(TimeSpan.FromSeconds(3))).ShouldBeEmpty();
    }
}
