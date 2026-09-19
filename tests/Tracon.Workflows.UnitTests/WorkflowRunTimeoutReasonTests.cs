using Microsoft.Extensions.AI;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// A timed-out workflow run records no reason a reader can act on.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 MEASURED, and it is a gap rather than a promise: the runner BUILDS a
/// <c>RunError</c> naming <c>Tracon:Workflows:RunTimeout</c>, but the closing
/// failure event carries no text and no payload, and the run record's
/// <c>Error</c> stays null. An operator sees a bare "Canceled".
/// </para>
/// <para>
/// Pinned here rather than left unwritten: this case is what turns the gap red
/// the day it is closed, and until then it stops anyone from assuming the
/// reason is already there.
/// </para>
/// <para>
/// It lives in its own file on purpose. <c>RunEventPayloadContractTests</c>
/// pairs "a file names an event type" with "a file mentions Payload" to decide
/// which event payloads a test pins; keeping this assertion beside the
/// event-sequence case in <c>WorkflowRunTimeoutTests</c> made that gate read a
/// coverage claim this file does not make.
/// </para>
/// </remarks>
public sealed class WorkflowRunTimeoutReasonTests
{
    [Fact]
    public async Task The_timed_out_run_carries_no_reason_a_reader_can_act_on()
    {
        // 🚨 MEASURED, and it is a gap, not a promise: the runner BUILDS a
        // RunError naming 'Tracon:Workflows:RunTimeout', but the closing
        // RunFailed event carries no text and no payload, and the run record's
        // Error stays null. An operator sees a bare "Canceled".
        //
        // Pinned here rather than left unwritten: this case is what turns the
        // gap red the day it is closed, and until then it stops anyone from
        // assuming the reason is already there.
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
        var failure = events[^1];

        failure.Type.ShouldBe(RunEventType.RunFailed);
        failure.Text.ShouldBeNull();
        failure.Payload.ShouldBeNull();

        var workflowRun = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(run => run.Kind == RunKind.Workflow);

        workflowRun.Error.ShouldBeNull();
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
