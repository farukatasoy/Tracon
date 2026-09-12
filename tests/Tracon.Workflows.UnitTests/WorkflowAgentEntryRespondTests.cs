using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// MT-WF-062: a <c>respond</c> call re-triggered the graph's ENTRY node (when it
/// is an agent-host) FROM SCRATCH, instead of resuming from the checkpoint.
/// </summary>
public sealed class WorkflowAgentEntryRespondTests
{
    [Fact]
    public async Task Respond_does_not_rerun_the_entry_node_when_it_is_an_agent()
    {
        var host = new WorkflowTestHost("summarizer");
        var summarizer = await host.Resolver.ResolveAsync("summarizer");

        var runner = host.CreateRunner(
            configure: null,
            services: null,
            AgentApprovalWorkflow.Registration(summarizer!));

        await Collect(runner, "summarizer-approval-flow", "report text");

        var first = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(run => run.Kind == RunKind.Workflow);

        var pending = (await runner.ListPendingRequestsAsync(first.Id)).Single();

        var resumed = new List<RunEvent>();

        await foreach (var runEvent in runner.RespondStreamingAsync(new WorkflowRespondRequest
        {
            RunId = first.Id,
            RequestId = pending.RequestId,
            Approved = true,
        }))
        {
            resumed.Add(runEvent);
        }

        // Root defect: 'respond' would rerun 'summarizer' FROM SCRATCH - producing a
        // second agent-kind 'runs' row AND an unanswered second WorkflowRequest, so
        // the run would end in AwaitingInput again (both assertions below catch this).
        var agentRuns = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Where(run => run.Kind == RunKind.Agent && string.Equals(run.AgentName, "summarizer", StringComparison.Ordinal))
            .ToList();

        agentRuns.ShouldHaveSingleItem();

        var second = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(run => run.Kind == RunKind.Workflow && run.Id != first.Id);

        second.Status.ShouldBe(RunStatus.Completed);

        resumed.Single(runEvent => runEvent.Type == RunEventType.WorkflowOutput).Text.ShouldBe("approved");
    }

    private static async Task Collect(WorkflowRunner runner, string name, string message)
    {
        await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest { WorkflowName = name, Message = message }))
        {
        }
    }
}
