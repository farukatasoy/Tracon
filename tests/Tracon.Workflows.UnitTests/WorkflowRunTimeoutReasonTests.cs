using Microsoft.Extensions.AI;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// A timed-out workflow run says why it stopped; a caller-cancelled one does not.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The deadline is the one cancellation nobody asked for, so it is the one
/// that has to name itself. Before this pair the runner BUILT a
/// <c>RunError</c> naming <c>Tracon:Workflows:RunTimeout</c> and then dropped
/// it: MAF ends its stream quietly when the token is cancelled, so the run
/// closed through the quiet-stop rewrite, which set the status and left the
/// error null. The operator saw a bare cancellation at full cost.
/// </para>
/// <para>
/// The second case is the half that keeps the first one honest. A run the
/// caller stopped carries NO reason, because the caller already knows the
/// reason; without this case, attaching the timeout text unconditionally
/// would satisfy the first assertion and lie about every manual cancel.
/// </para>
/// <para>
/// It lives in its own file on purpose. <c>RunEventPayloadContractTests</c>
/// pairs "a file names an event type" with "a file mentions Payload" to decide
/// which event payloads a test pins; keeping these assertions beside the
/// event-sequence case in <c>WorkflowRunTimeoutTests</c> made that gate read a
/// coverage claim this file does not make.
/// </para>
/// </remarks>
public sealed class WorkflowRunTimeoutReasonTests
{
    [Fact]
    public async Task The_timed_out_run_names_the_setting_that_stopped_it()
    {
        var host = new WorkflowTestHost("one");
        var slowStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        host.AddFunction<List<ChatMessage>, List<ChatMessage>>(
            "slow",
            async (messages, _, cancellationToken) =>
            {
                slowStarted.TrySetResult();
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);

                return messages;
            });

        await SaveChainAsync(host);

        var clock = new TriggerableTimeProvider();
        var runner = host.CreateRunnerWithTimeProvider(
            clock,
            static options => options.RunTimeout = TimeSpan.FromMinutes(5));

        var collecting = Collect(runner, "chain", "hello");
        await slowStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        clock.TriggerAll();

        var events = await collecting;
        var closing = events[^1];

        closing.Type.ShouldBe(RunEventType.RunFailed);
        closing.Text.ShouldNotBeNull();
        closing.Text.ShouldContain("Tracon:Workflows:RunTimeout");

        var workflowRun = await SingleWorkflowRunAsync(host);

        // The deadline stops a run; it does not fault one. The reason is what
        // tells the two kinds of stop apart, not the status.
        workflowRun.Status.ShouldBe(RunStatus.Canceled);
        workflowRun.Error.ShouldNotBeNull();
        workflowRun.Error.Type.ShouldBe(nameof(TimeoutException));
        workflowRun.Error.Message.ShouldContain("Tracon:Workflows:RunTimeout");
    }

    [Fact]
    public async Task The_run_the_caller_stopped_carries_no_reason()
    {
        var host = new WorkflowTestHost("one");

        using var caller = new CancellationTokenSource();

        host.AddFunction<List<ChatMessage>, List<ChatMessage>>(
            "slow",
            async (messages, _, cancellationToken) =>
            {
                // Cancel from inside the graph: the stop has to land while a
                // node is running, which is where a dropped client lands too.
                await caller.CancelAsync().ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);

                return messages;
            });

        await SaveChainAsync(host);

        // Long enough that the deadline cannot be the thing that fires.
        var runner = host.CreateRunner(static options => options.RunTimeout = TimeSpan.FromMinutes(5));

        // Measured, and deliberately not asserted: whether the stream ends
        // quietly or surfaces an OperationCanceledException depends on whether
        // an event append is in flight when the stop lands, so pinning either
        // one would be a coin toss. This case is about the row the run leaves
        // behind, which is written on both paths.
        try
        {
            await Collect(runner, "chain", "hello", caller.Token);
        }
        catch (OperationCanceledException)
        {
        }

        var workflowRun = await SingleWorkflowRunAsync(host);

        workflowRun.Status.ShouldBe(RunStatus.Canceled);
        workflowRun.Error.ShouldBeNull();
    }

    private static async Task SaveChainAsync(WorkflowTestHost host)
        => await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            Nodes =
            [
                new WorkflowNodeReference { Name = "one", Kind = WorkflowNodeKind.Agent },
                new WorkflowNodeReference { Name = "slow", Kind = WorkflowNodeKind.Function },
            ],
        });

    private static async Task<RunRecord> SingleWorkflowRunAsync(WorkflowTestHost host)
        => (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(run => run.Kind == RunKind.Workflow);

    private static async Task<List<RunEvent>> Collect(
        WorkflowRunner runner,
        string name,
        string message,
        CancellationToken cancellationToken = default)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in runner.RunStreamingAsync(
            new WorkflowRunRequest { WorkflowName = name, Message = message }, cancellationToken))
        {
            events.Add(runEvent);
        }

        return events;
    }
}
