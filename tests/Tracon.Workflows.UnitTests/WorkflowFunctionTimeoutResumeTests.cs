using Microsoft.Extensions.AI;
using Tracon.Workflows.UnitTests.Fakes;
using static Tracon.Workflows.UnitTests.Fakes.TimeoutRuns;
using static Tracon.Workflows.UnitTests.WorkflowFunctionTimeoutTests;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// A function node's timeout is not part of its identity (failure mode 10):
/// changing it leaves an old checkpoint usable. The resumed run takes the value of the
/// version it recorded, or of the version a resume names on purpose.
/// </summary>
/// <remarks>
/// The node's NAME is its executor id; the timeout is applied by the wrapper built for
/// each compile. Measured, like the settings (<see cref="WorkflowStructuralChangeResumeTests"/>),
/// rather than assumed: a field that reached the id would make every waiting run
/// unresumable after a timeout edit.
/// </remarks>
public sealed class WorkflowFunctionTimeoutResumeTests
{
    [Fact]
    public async Task Changing_a_nodes_timeout_leaves_the_old_checkpoint_usable_and_a_resume_that_names_the_new_version_takes_the_new_value()
    {
        var clock = new ManualClock();
        var host = Host(clock, logs: null, "one");
        var hang = false;
        var started = Signal();

        host.AddFunctionWithOptions<List<ChatMessage>, List<ChatMessage>>(
            "pass",
            async (messages, _, cancellationToken) =>
            {
                if (Volatile.Read(ref hang))
                {
                    started.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); // delay: simulated
                }

                return messages;
            },
            static options => options.Description = "Passes the messages on.");

        await SaveAsync(host, "chain", Agent("one"), Function("pass", timeout: TimeSpan.FromSeconds(30)));

        var first = await Collect(host.CreateRunnerWithTimeProvider(clock), "chain");
        var source = await RunOf(host, first);

        source.Status.ShouldBe(RunStatus.Completed);

        // The checkpoint written after the agent's super-step: resuming from it
        // runs the function node again.
        var checkpoint = (await host.CheckpointStore.ListByRunAsync(host.TenantContext.TenantId, source.Id))[0];

        var edited = await SaveAsync(host, "chain", Agent("one"), Function("pass", timeout: TimeSpan.FromSeconds(2)));
        Volatile.Write(ref hang, true);

        var resuming = ResumeAsync(host, clock, source.Id, checkpoint.CheckpointId, edited.Version);

        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await ArmedAsync(clock, TimeSpan.FromSeconds(2));
        clock.Advance(TimeSpan.FromSeconds(2));

        var resumed = await RunOf(host, await resuming.WaitAsync(TestContext.Current.CancellationToken));

        // Not "the graph's structure differs": the edit was not structural, the
        // resume compiled, ran the node, and the node ran under the NEW timeout.
        resumed.Id.ShouldNotBe(source.Id);
        resumed.Status.ShouldBe(RunStatus.Failed);
        resumed.Error!.Type.ShouldBe(TraconWorkflowFunctionTimeoutException.WorkflowFunctionTimeoutErrorType);
        resumed.Error.Message.ShouldContain("2 s");
    }

    [Fact]
    public async Task A_resume_keeps_the_timeout_of_the_version_the_run_recorded()
    {
        // The operator shortened the timeout while the run waited. Nothing named the new
        // version, so the node still has the 30 seconds the run was started with.
        var clock = new ManualClock();
        var host = Host(clock, logs: null, "one");
        var hang = false;
        var started = Signal();

        host.AddFunctionWithOptions<List<ChatMessage>, List<ChatMessage>>(
            "pass",
            async (messages, _, cancellationToken) =>
            {
                if (Volatile.Read(ref hang))
                {
                    started.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); // delay: simulated
                }

                return messages;
            },
            static options => options.Description = "Passes the messages on.");

        await SaveAsync(host, "chain", Agent("one"), Function("pass", timeout: TimeSpan.FromSeconds(30)));

        var source = await RunOf(host, await Collect(host.CreateRunnerWithTimeProvider(clock), "chain"));
        var checkpoint = (await host.CheckpointStore.ListByRunAsync(host.TenantContext.TenantId, source.Id))[0];

        await SaveAsync(host, "chain", Agent("one"), Function("pass", timeout: TimeSpan.FromSeconds(2)));
        Volatile.Write(ref hang, true);

        var resuming = ResumeAsync(host, clock, source.Id, checkpoint.CheckpointId);

        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await ArmedAsync(clock, TimeSpan.FromSeconds(30));
        clock.Advance(TimeSpan.FromSeconds(30));

        var resumed = await RunOf(host, await resuming.WaitAsync(TestContext.Current.CancellationToken));

        resumed.Status.ShouldBe(RunStatus.Failed);
        resumed.Error!.Type.ShouldBe(TraconWorkflowFunctionTimeoutException.WorkflowFunctionTimeoutErrorType);
        resumed.Error.Message.ShouldContain("30 s");
    }

    [Fact]
    public async Task Adding_a_timeout_to_a_node_that_had_none_leaves_the_old_checkpoint_usable()
    {
        var clock = new ManualClock();
        var host = Host(clock, logs: null, "one");

        host.AddFunction<List<ChatMessage>, List<ChatMessage>>(
            "pass",
            static (messages, _, _) => new ValueTask<List<ChatMessage>>(messages));

        await SaveAsync(host, "chain", Agent("one"), Function("pass"));

        var source = await RunOf(host, await Collect(host.CreateRunnerWithTimeProvider(clock), "chain"));
        var checkpoint = (await host.CheckpointStore.ListByRunAsync(host.TenantContext.TenantId, source.Id))[0];

        var edited = await SaveAsync(host, "chain", Agent("one"), Function("pass", timeout: TimeSpan.FromMinutes(1)));

        var resumed = await RunOf(host, await ResumeAsync(host, clock, source.Id, checkpoint.CheckpointId, edited.Version));

        resumed.Status.ShouldBe(RunStatus.Completed);
    }

    internal static async Task<List<RunEvent>> ResumeAsync(
        WorkflowTestHost host,
        TimeProvider clock,
        Guid runId,
        string checkpointId,
        int? definitionVersion = null)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in host.CreateRunnerWithTimeProvider(clock).ResumeStreamingAsync(new WorkflowResumeRequest
        {
            RunId = runId,
            CheckpointId = checkpointId,
            DefinitionVersion = definitionVersion,
        }))
        {
            events.Add(runEvent);
        }

        return events;
    }
}
