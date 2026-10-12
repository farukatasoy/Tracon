using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// What a structural edit of a stored workflow does to a run that is waiting to be resumed
/// (F-292, section 195.4 of the phase document).
/// </summary>
/// <remarks>
/// <para>
/// MEASURED, not assumed: the console warns before it saves a structural change, and the
/// wording of that warning depends on whether the edit makes an old checkpoint unusable.
/// The plan asked "does adding or removing an agent already do this today?" - these tests are
/// the answer, and they stay as the alarm that fires if Microsoft Agent Framework ever changes
/// what it compares.
/// </para>
/// <para>
/// Not structural, and therefore compatible: a settings change
/// (<see cref="WorkflowFunctionSettingsTests"/>) and a description change.
/// </para>
/// </remarks>
public sealed class WorkflowStructuralChangeResumeTests
{
    [Fact]
    public async Task Adding_an_agent_to_an_agent_only_chain_makes_the_old_checkpoint_unusable()
    {
        var host = new WorkflowTestHost("one", "two", "three");

        await host.SaveAsync(AgentOnly("chain", "one", "two"));

        var checkpoint = await RunAndPickFirstCheckpointAsync(host);

        await host.SaveAsync(AgentOnly("chain", "one", "two", "three"));

        (await ResumeFailureAsync(host, checkpoint)).ShouldNotBeNull().ShouldContain("structure differs", Case.Sensitive);
    }

    [Fact]
    public async Task Writing_the_same_agent_only_chain_as_nodes_makes_the_old_checkpoint_unusable()
    {
        // The console's canonical form changes with the chain's content (agent-only chains are
        // saved as agentNames, chains with a function as nodes). The built-in pattern and the
        // hand-built graph do not share executor ids, so the SAME agents in the other form do
        // not resume either - which is why the editor says so when it changes the form.
        var host = new WorkflowTestHost("one", "two");

        await host.SaveAsync(AgentOnly("chain", "one", "two"));

        var checkpoint = await RunAndPickFirstCheckpointAsync(host);

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            Nodes =
            [
                new WorkflowNodeReference { Name = "one", Kind = WorkflowNodeKind.Agent },
                new WorkflowNodeReference { Name = "two", Kind = WorkflowNodeKind.Agent },
            ],
        });

        (await ResumeFailureAsync(host, checkpoint)).ShouldNotBeNull().ShouldContain("structure differs", Case.Sensitive);
    }

    [Fact]
    public async Task Adding_a_node_to_a_node_chain_makes_the_old_checkpoint_unusable()
    {
        var host = new WorkflowTestHost("one", "two");

        host.AddFunction<List<ChatMessage>, List<ChatMessage>>(
            "pass",
            static (messages, _, _) => new ValueTask<List<ChatMessage>>(messages));

        await host.SaveAsync(NodeChain("chain", ("one", WorkflowNodeKind.Agent), ("pass", WorkflowNodeKind.Function)));

        var checkpoint = await RunAndPickFirstCheckpointAsync(host);

        await host.SaveAsync(NodeChain(
            "chain",
            ("one", WorkflowNodeKind.Agent),
            ("pass", WorkflowNodeKind.Function),
            ("two", WorkflowNodeKind.Agent)));

        (await ResumeFailureAsync(host, checkpoint)).ShouldNotBeNull().ShouldContain("structure differs", Case.Sensitive);
    }

    [Fact]
    public async Task A_description_change_leaves_the_old_checkpoint_usable()
    {
        var host = new WorkflowTestHost("one", "two");

        await host.SaveAsync(AgentOnly("chain", "one", "two"));

        var checkpoint = await RunAndPickFirstCheckpointAsync(host);

        await host.SaveAsync(AgentOnly("chain", "one", "two") with { Description = "Reworded." });

        (await ResumeFailureAsync(host, checkpoint)).ShouldBeNull();
    }

    private static async Task<string> RunAndPickFirstCheckpointAsync(WorkflowTestHost host)
    {
        await foreach (var _ in host.CreateRunner().RunStreamingAsync(new WorkflowRunRequest { WorkflowName = "chain", Message = "hello" }))
        {
        }

        var root = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(static run => run.Kind == RunKind.Workflow);

        root.Status.ShouldBe(RunStatus.Completed);

        var checkpoints = await host.CheckpointStore.ListByRunAsync(host.TenantContext.TenantId, root.Id);

        checkpoints.ShouldNotBeEmpty();

        return $"{root.Id}|{checkpoints[0].CheckpointId}";
    }

    /// <summary>Resumes from the checkpoint; returns the error the run reports, or <see langword="null"/> when it resumed.</summary>
    private static async Task<string?> ResumeFailureAsync(WorkflowTestHost host, string runAndCheckpoint)
    {
        var parts = runAndCheckpoint.Split('|');
        var events = new List<RunEvent>();

        try
        {
            await foreach (var runEvent in host.CreateRunner().ResumeStreamingAsync(new WorkflowResumeRequest
            {
                RunId = Guid.Parse(parts[0]),
                CheckpointId = parts[1],
            }))
            {
                events.Add(runEvent);
            }
        }
        catch (TraconException exception)
        {
            return exception.Message;
        }

        return events.FirstOrDefault(static runEvent => runEvent.Type == RunEventType.RunFailed)?.Text
               ?? (events.Any(static runEvent => runEvent.Type == RunEventType.RunFailed) ? "failed" : null);
    }

    private static WorkflowDefinition AgentOnly(string name, params string[] agents)
        => new() { Name = name, Kind = WorkflowKind.Sequential, AgentNames = agents };

    private static WorkflowDefinition NodeChain(string name, params (string Name, WorkflowNodeKind Kind)[] nodes)
        => new()
        {
            Name = name,
            Kind = WorkflowKind.Sequential,
            Nodes = [.. nodes.Select(static node => new WorkflowNodeReference { Name = node.Name, Kind = node.Kind })],
        };
}
