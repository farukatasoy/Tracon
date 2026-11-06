using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tracon.Tests.Workflows;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// Cancels a workflow run at every await point it has, one run per point, and checks the same things each time: every
/// run row reaches a terminal state or rests waiting for a human, the caller sees a cancellation or a result and nothing
/// else, and nothing is left registered, open, armed or holding a slot.
/// </summary>
/// <remarks>
/// <para>
/// The sweep itself is <see cref="WorkflowCancellationSweep"/>, shared with the sweeps that run it on real databases;
/// this class runs it in memory, over every graph, for a new run and for the continuation of a run that waits, with the
/// two kinds of store (one that ignores its token and one that throws when its token is already canceled, as a SQL
/// store does) and the two ways to cancel (the caller's token, and the operator's cancel through the registry). The
/// harness is in memory: it has no process, connection or file to leak, and the sweep does not claim to look for any.
/// </para>
/// </remarks>
public sealed class WorkflowCancellationSweepTests
{
    private static readonly TimeSpan RunBound = TimeSpan.FromSeconds(20);

    /// <summary>Gets every combination of graph, start, kind of store and way to cancel.</summary>
    /// <returns>The combinations.</returns>
    public static TheoryData<SweepGraph, SweepStart, bool, bool> Combinations()
    {
        var data = new TheoryData<SweepGraph, SweepStart, bool, bool>();

        foreach (var graph in Enum.GetValues<SweepGraph>())
        {
            foreach (var start in Enum.GetValues<SweepStart>())
            {
                if (start == SweepStart.Resume && graph is not (SweepGraph.AwaitingInput or SweepGraph.ToolApproval))
                {
                    continue;
                }

                foreach (var storeHonorsToken in new[] { false, true })
                {
                    foreach (var operatorCancel in new[] { false, true })
                    {
                        data.Add(graph, start, storeHonorsToken, operatorCancel);
                    }
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task A_workflow_run_cancelled_at_any_await_point_ends_closed_or_waiting_and_leaks_nothing(
        SweepGraph graph,
        SweepStart start,
        bool storeHonorsToken,
        bool operatorCancel)
    {
        await using var backend = new InMemorySweepBackend();

        var report = await WorkflowCancellationSweep.SweepAsync(backend, graph, start, operatorCancel, storeHonorsToken);

        TestContext.Current.SendDiagnosticMessage($"{graph} {start}: {report.AwaitPoints} await points ({string.Join(", ", report.Endings.Select(static ending => $"{ending.Key} {ending.Value}"))}), {report.Elapsed.TotalSeconds:0.0} s (honoring store: {storeHonorsToken}, operator cancel: {operatorCancel})");

        report.AwaitPoints.ShouldBeGreaterThan(MinimumAwaitPoints(graph), "the run must cross the stores, the sink and the agents of its graph");
        report.Problems.ShouldBeEmpty();
    }

    /// <summary>
    /// The container disposes the runner when the host stops; a run still in flight then gives its slot back to a
    /// semaphore that is gone. The give-back sits in the <c>finally</c> that also closes the run, so an exception from
    /// it used to leave the row at Running and hand the caller an <see cref="ObjectDisposedException"/>.
    /// </summary>
    [Fact]
    public async Task A_runner_disposed_while_a_run_is_in_flight_still_closes_the_run()
    {
        var plan = new FaultPlan();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new WorkflowTestHost();

        host.AddAgent("first", new SweepAgent("first", plan) { Entered = entered, Hold = hold.Task });
        host.AddAgent("second", new SweepAgent("second", plan));

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["first", "second"],
        });

        var catalog = new WorkflowCatalog(
            [],
            host.DefinitionStore,
            host.Compiler,
            host.TenantContext,
            new ServiceCollection().BuildServiceProvider());

        var runner = new WorkflowRunner(
            catalog,
            host.RunStore,
            host.CheckpointStore,
            host.TenantContext,
            Options.Create(new TraconWorkflowOptions { MaxConcurrentRuns = 1 }),
            Options.Create(new TraconOptions()),
            NullLogger<WorkflowRunner>.Instance,
            agentCache: host.AgentCache);

        var running = Task.Run(
            () => WorkflowCancellationSweep.DriveAsync(runner.RunStreamingAsync(new WorkflowRunRequest { WorkflowName = "chain", Message = "hello" }, CancellationToken.None)),
            CancellationToken.None);

        await entered.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

        runner.Dispose();
        hold.SetResult();

        await Should.NotThrowAsync(async () => await running.WaitAsync(RunBound, TestContext.Current.CancellationToken));

        var runs = await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 });

        runs.ShouldNotBeEmpty();
        runs.ShouldAllBe(static run => run.Status != RunStatus.Running && run.Status != RunStatus.Queued, "no run row is left open");
    }

    /// <summary>
    /// The fewest awaited calls a run of the graph can be expected to make (about a third of what it makes today); a count
    /// below it means the plan lost a store or the graph stopped reaching one.
    /// </summary>
    private static int MinimumAwaitPoints(SweepGraph graph)
        => graph switch
        {
            SweepGraph.Sequential => 25,
            SweepGraph.FunctionNode => 16,
            SweepGraph.Handoff => 30,
            SweepGraph.AwaitingInput => 12,
            SweepGraph.ToolApproval => 15,
            _ => throw new ArgumentOutOfRangeException(nameof(graph), graph, "Unknown sweep graph."),
        };
}
