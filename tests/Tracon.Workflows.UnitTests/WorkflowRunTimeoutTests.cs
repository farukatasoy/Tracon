using Microsoft.Extensions.AI;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// What <see cref="TraconWorkflowOptions.RunTimeout"/> actually does.
/// </summary>
/// <remarks>
/// 🚨 The setting had <strong>no test at all</strong> while its documentation
/// called it "the maximum duration of a single workflow run" without
/// qualification. Everything asserted here was MEASURED against the runner,
/// not derived from that sentence.
/// </remarks>
public sealed class WorkflowRunTimeoutTests
{
    [Fact]
    public async Task A_run_past_the_deadline_stops_inside_the_node_that_overran()
    {
        var host = new WorkflowTestHost("one");

        host.AddFunction<List<ChatMessage>, List<ChatMessage>>(
            "slow",
            static async (messages, _, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);

                return messages;
            });

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            Nodes =
            [
                new WorkflowNodeReference { Name = "one", Kind = WorkflowNodeKind.Agent },
                new WorkflowNodeReference { Name = "slow", Kind = WorkflowNodeKind.Function },
            ],
        });

        var runner = host.CreateRunner(static options => options.RunTimeout = TimeSpan.FromMilliseconds(200));

        var events = await Collect(runner, "chain", "hello");

        // The stream ends INSIDE the slow node: it was entered and never
        // completed. The first node's pair is intact, so this is the deadline
        // biting and not the graph failing to start.
        events.Select(runEvent => runEvent.Type).ShouldBe(
        [
            RunEventType.RunStarted,
            RunEventType.WorkflowStarted,
            RunEventType.SuperStepStarted,
            RunEventType.ExecutorInvoked,
            RunEventType.ExecutorCompleted,
            RunEventType.SuperStepCompleted,
            RunEventType.SuperStepStarted,
            RunEventType.ExecutorInvoked,
            RunEventType.RunFailed,
        ]);

        var workflowRun = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(run => run.Kind == RunKind.Workflow);

        workflowRun.Status.ShouldBe(RunStatus.Canceled);
    }

    [Fact]
    public async Task A_run_inside_the_deadline_is_untouched()
    {
        // The other half: a deadline that never fires must not change the
        // outcome. Without this, a timeout that fired on EVERY run would still
        // satisfy the case above.
        var host = new WorkflowTestHost("one");

        host.AddFunction<List<ChatMessage>, List<ChatMessage>>(
            "quick",
            static (messages, _, _) => new ValueTask<List<ChatMessage>>(messages));

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            Nodes =
            [
                new WorkflowNodeReference { Name = "one", Kind = WorkflowNodeKind.Agent },
                new WorkflowNodeReference { Name = "quick", Kind = WorkflowNodeKind.Function },
            ],
        });

        var runner = host.CreateRunner(static options => options.RunTimeout = TimeSpan.FromMinutes(5));

        await Collect(runner, "chain", "hello");

        var workflowRun = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(run => run.Kind == RunKind.Workflow);

        workflowRun.Status.ShouldBe(RunStatus.Completed);
    }

    private static async Task<List<RunEvent>> Collect(WorkflowRunner runner, string name, string message)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in runner.RunStreamingAsync(
            new WorkflowRunRequest { WorkflowName = name, Message = message }))
        {
            events.Add(runEvent);
        }

        return events;
    }
}
