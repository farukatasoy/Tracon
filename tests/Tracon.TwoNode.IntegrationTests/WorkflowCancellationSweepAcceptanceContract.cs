using Tracon.Tests.Workflows;
using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>
/// A workflow run that is canceled at any of its awaited calls ends closed or waiting, and leaves nothing behind, on a real
/// database server.
/// </summary>
/// <remarks>
/// <para>
/// The sweep is <see cref="WorkflowCancellationSweep"/>: it cancels a run at each of the calls it makes on its run store,
/// its checkpoint store, its inbox of requests, its ledger of answers, its event sink and its agents, one run per call, and
/// checks that no run row is left running, that nothing is registered, open or armed, and that the run's slot is free.
/// The in-memory store and SQLite run it in the workflow unit tests; here it runs where a server's driver and pool take part
/// in the cancel: a statement that is sent and then abandoned, a connection that must go back to the pool, a transaction
/// that must end.
/// </para>
/// <para>
/// The stores throw by themselves when their token is already canceled, so the two kinds of store the in-memory sweep tells
/// apart are one here. Besides the invariants of the sweep, the server is asked after every run whether a connection is
/// still checked out, a session still in a transaction or a lock still held. No host is started: nothing but the sweep
/// writes to the schema.
/// </para>
/// </remarks>
public abstract class WorkflowCancellationSweepAcceptanceContract
{
    /// <summary>The environment variable that makes a provider that sweeps half of the combinations sweep all of them.</summary>
    /// <remarks>CI sets it to <c>1</c>; a plain local run leaves it unset.</remarks>
    public const string FullSweepVariable = "TRACON_TEST_FULL_SWEEP";

    /// <summary>Gets whether this run sweeps every combination on every provider (<see cref="FullSweepVariable"/> is <c>1</c>).</summary>
    protected static bool FullSweep
        => string.Equals(Environment.GetEnvironmentVariable(FullSweepVariable), "1", StringComparison.Ordinal);

    /// <summary>Gets the combinations the theory runs: every combination of graph, start and way to cancel.</summary>
    /// <returns>The combinations.</returns>
    /// <remarks>
    /// A provider class may hide this member with one of its own: the theory reads the member by name from the class that
    /// runs it, before its base.
    /// </remarks>
    public static TheoryData<SweepGraph, SweepStart, bool> Combinations() => AllCombinations();

    /// <summary>
    /// Gets half of the combinations: every graph and both starts, each with one of the two ways to cancel, and each way
    /// to cancel with both starts.
    /// </summary>
    /// <returns>The combinations whose way to cancel alternates with the graph and the start.</returns>
    /// <remarks>
    /// The two ways to cancel reach the same awaited calls; they differ in who asks for the cancel. Alternating them keeps
    /// every awaited call of every graph swept on the server, through one of the two.
    /// </remarks>
    internal static TheoryData<SweepGraph, SweepStart, bool> HalfOfCombinations()
    {
        var data = new TheoryData<SweepGraph, SweepStart, bool>();

        foreach (var row in AllCombinations())
        {
            var (graph, start, operatorCancel) = row.Data;

            if (operatorCancel == (((int)graph + (int)start) % 2 == 1))
            {
                data.Add(graph, start, operatorCancel);
            }
        }

        return data;
    }

    private static TheoryData<SweepGraph, SweepStart, bool> AllCombinations()
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

    /// <summary>Gets a schema no other test uses, on the class's server.</summary>
    /// <returns>The database the stores connect to.</returns>
    protected abstract TwoNodeDatabase NewDatabase();

    /// <summary>
    /// Gets a statement that takes a lock the server holds until the transaction that ran it ends: what a run canceled in
    /// the middle of a write would leave held.
    /// </summary>
    protected abstract string LockStatement { get; }

    /// <summary>A run canceled at any of its awaited calls ends closed or waiting, and nothing it opened is left open.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="start">Whether the swept run is new or continues a run that waits for an answer.</param>
    /// <param name="operatorCancel">Whether the run is canceled through the registry and not by its caller's token.</param>
    /// <returns>A task that completes when the sweep is done.</returns>
    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task A_workflow_run_cancelled_at_any_await_point_ends_closed_or_waiting_and_leaks_nothing(
        SweepGraph graph,
        SweepStart start,
        bool operatorCancel)
    {
        await using var backend = await NewDatabase().OpenSweepBackendAsync();

        var report = await WorkflowCancellationSweep.SweepAsync(backend, graph, start, operatorCancel, storeHonorsToken: false);

        TestContext.Current.SendDiagnosticMessage($"{backend.Name} {graph} {start}: {report.AwaitPoints} await points ({string.Join(", ", report.Endings.Select(static ending => $"{ending.Key} {ending.Value}"))}), (operator cancel: {operatorCancel})");

        report.Problems.ShouldBeEmpty();
    }

    /// <summary>
    /// The check for what a run leaves in the pool and on the server has to see it. A connection that is checked out, a
    /// transaction nobody ended and a lock nobody released are what a run canceled in the middle of a write would leave
    /// behind.
    /// </summary>
    /// <returns>A task that completes when all three are reported, and gone again once they end.</returns>
    [Fact]
    public async Task A_connection_never_given_back_a_transaction_never_ended_and_a_lock_never_released_are_reported_as_leaks()
    {
        await using var backend = await NewDatabase().OpenSweepBackendAsync();

        await using (var connection = await backend.DataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = LockStatement;
                await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
            }

            var leaks = await backend.FindLeaksAsync(TimeSpan.FromMilliseconds(500));

            leaks.ShouldContain(static leak => leak.Contains("connection", StringComparison.Ordinal));
            leaks.ShouldContain(static leak => leak.Contains("transaction", StringComparison.Ordinal));
            leaks.ShouldContain(static leak => leak.Contains("lock", StringComparison.Ordinal));
        }

        (await backend.FindLeaksAsync(TimeSpan.FromSeconds(5))).ShouldBeEmpty();
    }
}
