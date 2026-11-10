using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// A workflow run asks the store where an earlier attempt stopped only when the caller chose the run's id: an id the
/// runner made for the run has no earlier attempt, and the question is a round-trip paid before the first event.
/// </summary>
public sealed class WorkflowStartLookupTests
{
    [Fact]
    public async Task A_run_whose_id_the_runner_made_does_not_ask_the_store_for_a_sequence_that_cannot_exist()
    {
        var host = await HostWithWorkflowAsync();
        var store = new LookupCountingRunStore(host.RunStore);

        await RunAsync(host.CreateRunnerWith(runStore: store), runId: null);

        store.LastSequenceLookups.ShouldBe(0);

        var root = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = true })).ShouldHaveSingleItem();
        await foreach (var runEvent in host.RunStore.ReadEventsAsync(root.Id))
        {
            runEvent.Sequence.ShouldBe(0, "the stream still starts at zero without the question");
            break;
        }
    }

    [Fact]
    public async Task A_run_whose_id_the_caller_chose_still_asks_because_the_id_may_be_a_retry()
    {
        var host = await HostWithWorkflowAsync();
        var store = new LookupCountingRunStore(host.RunStore);

        await RunAsync(host.CreateRunnerWith(runStore: store), runId: TraconId.NewId());

        store.LastSequenceLookups.ShouldBe(1);
    }

    private static async Task<WorkflowTestHost> HostWithWorkflowAsync()
    {
        var host = new WorkflowTestHost("writer", "editor");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["writer", "editor"],
        });

        return host;
    }

    private static async Task RunAsync(WorkflowRunner runner, Guid? runId)
    {
        await foreach (var _ in runner.RunStreamingAsync(
            new WorkflowRunRequest { WorkflowName = "chain", Message = "hello", RunId = runId },
            TestContext.Current.CancellationToken))
        {
            // drain the stream
        }
    }
}
