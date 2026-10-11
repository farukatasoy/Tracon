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
        var slowInvocationRecorded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        host.AddFunction<List<ChatMessage>, List<ChatMessage>>(
            "slow",
            async (messages, _, cancellationToken) =>
            {
                slowStarted.TrySetResult();
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false); // delay: simulated

                return messages;
            });

        await SaveChainAsync(host);

        var clock = new TriggerableTimeProvider();
        var runner = host.CreateRunnerWithTimeProvider(
            clock,
            static options => options.RunTimeout = TimeSpan.FromMinutes(5));

        var collecting = Collect(runner, "chain", "hello", observed: runEvent =>
        {
            if (runEvent.Type == RunEventType.ExecutorInvoked &&
                string.Equals(runEvent.Text, "slow", StringComparison.Ordinal))
            {
                slowInvocationRecorded.TrySetResult();
            }
        });

        // MAF can invoke the handler before it publishes ExecutorInvoked.
        // Both facts must be true before the test fires the runner deadline.
        await Task.WhenAll(slowStarted.Task, slowInvocationRecorded.Task)
            .WaitAsync(TestContext.Current.CancellationToken);
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
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false); // delay: simulated

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
            await Collect(runner, "chain", "hello", cancellationToken: caller.Token);
        }
        catch (OperationCanceledException)
        {
        }

        var workflowRun = await SingleWorkflowRunAsync(host);

        workflowRun.Status.ShouldBe(RunStatus.Canceled);
        workflowRun.Error.ShouldBeNull();
    }

    [Fact]
    public async Task A_run_that_times_out_waiting_for_a_slot_is_recorded_and_names_the_setting()
    {
        // F-325: the slot wait ran BEFORE the run row opened. A run whose
        // deadline passed while it queued ended with an
        // OperationCanceledException and no event at all: the stream had no
        // RunStarted, no closing frame, and the run id the endpoint had already
        // announced pointed at no row.
        var host = new WorkflowTestHost("one");
        var holding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queuedStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        host.AddFunction<List<ChatMessage>, List<ChatMessage>>(
            "slow",
            async (messages, _, _) =>
            {
                // Ignores its token on purpose: the first run must keep the
                // only slot after the deadline fires for both runs.
                holding.TrySetResult();
                await release.Task.ConfigureAwait(false);

                return messages;
            });

        await SaveChainAsync(host);

        var clock = new TriggerableTimeProvider();
        var runner = host.CreateRunnerWithTimeProvider(
            clock,
            static options =>
            {
                options.MaxConcurrentRuns = 1;
                options.RunTimeout = TimeSpan.FromMinutes(5);
            });

        var first = Collect(runner, "chain", "hello");

        await holding.Task.WaitAsync(TestContext.Current.CancellationToken);

        var queued = Collect(runner, "chain", "hello", observed: runEvent =>
        {
            if (runEvent.Type == RunEventType.RunStarted)
            {
                queuedStarted.TrySetResult();
            }
        });

        // The queued run says it has started while it still waits for the slot.
        await queuedStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        clock.TriggerAll();

        var events = await queued.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        release.TrySetResult();
        await first.WaitAsync(TestContext.Current.CancellationToken);

        var closing = events[^1];

        events[0].Type.ShouldBe(RunEventType.RunStarted);
        closing.Type.ShouldBe(RunEventType.RunFailed);
        closing.Text.ShouldNotBeNull();
        closing.Text.ShouldContain("Tracon:Workflows:RunTimeout");

        var queuedRun = await host.RunStore.GetRunAsync(events[0].RunId, TestContext.Current.CancellationToken);

        queuedRun.ShouldNotBeNull();
        queuedRun.Status.ShouldBe(RunStatus.Canceled);
        queuedRun.Error.ShouldNotBeNull();
        queuedRun.Error.Type.ShouldBe(nameof(TimeoutException));
        queuedRun.Error.Message.ShouldContain("Tracon:Workflows:RunTimeout");
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
        Action<RunEvent>? observed = null,
        CancellationToken cancellationToken = default)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in runner.RunStreamingAsync(
            new WorkflowRunRequest { WorkflowName = name, Message = message }, cancellationToken))
        {
            events.Add(runEvent);
            observed?.Invoke(runEvent);
        }

        return events;
    }
}
