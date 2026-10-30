using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// What a structural edit of a stored workflow does to a run that is waiting to be resumed
/// (an earlier plan of the phase document; an earlier change).
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
/// Since then a run resumes with the version it recorded, so the edit no longer strands it:
/// every test below proves BOTH halves - the recorded version resumes, and the edited version,
/// named on purpose, is the one the checkpoint does not fit.
/// </para>
/// <para>
/// Not structural, and therefore compatible with either version: a settings change
/// (<see cref="WorkflowFunctionSettingsTests"/>), a description change and an agent version pin.
/// </para>
/// </remarks>
public sealed class WorkflowStructuralChangeResumeTests
{
    [Fact]
    public async Task Adding_an_agent_to_an_agent_only_chain_makes_the_old_checkpoint_unusable_for_the_edited_version()
    {
        var host = new WorkflowTestHost("one", "two", "three");

        await host.SaveAsync(AgentOnly("chain", "one", "two"));

        var checkpoint = await RunAndPickFirstCheckpointAsync(host);

        await host.SaveAsync(AgentOnly("chain", "one", "two", "three"));

        await AssertTheRecordedVersionResumesAndTheEditedOneDoesNotAsync(host, checkpoint);
    }

    [Fact]
    public async Task Writing_the_same_agent_only_chain_as_nodes_makes_the_old_checkpoint_unusable_for_the_edited_version()
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

        await AssertTheRecordedVersionResumesAndTheEditedOneDoesNotAsync(host, checkpoint);
    }

    [Fact]
    public async Task Adding_a_node_to_a_node_chain_makes_the_old_checkpoint_unusable_for_the_edited_version()
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

        await AssertTheRecordedVersionResumesAndTheEditedOneDoesNotAsync(host, checkpoint);
    }

    [Fact]
    public async Task Reordering_the_agents_of_a_sequential_chain_makes_the_old_checkpoint_unusable_for_the_edited_version()
    {
        var host = new WorkflowTestHost("one", "two");

        await host.SaveAsync(AgentOnly("chain", "one", "two"));

        var checkpoint = await RunAndPickFirstCheckpointAsync(host);

        await host.SaveAsync(AgentOnly("chain", "two", "one"));

        await AssertTheRecordedVersionResumesAndTheEditedOneDoesNotAsync(host, checkpoint);
    }

    [Fact]
    public async Task Removing_an_agent_from_a_sequential_chain_makes_the_old_checkpoint_unusable_for_the_edited_version()
    {
        var host = new WorkflowTestHost("one", "two", "three");

        await host.SaveAsync(AgentOnly("chain", "one", "two", "three"));

        var checkpoint = await RunAndPickFirstCheckpointAsync(host);

        await host.SaveAsync(AgentOnly("chain", "one", "two"));

        await AssertTheRecordedVersionResumesAndTheEditedOneDoesNotAsync(host, checkpoint);
    }

    [Fact]
    public async Task Removing_a_node_from_a_node_chain_makes_the_old_checkpoint_unusable_for_the_edited_version()
    {
        var host = new WorkflowTestHost("one", "two");

        host.AddFunction<List<ChatMessage>, List<ChatMessage>>(
            "pass",
            static (messages, _, _) => new ValueTask<List<ChatMessage>>(messages));

        await host.SaveAsync(NodeChain(
            "chain",
            ("one", WorkflowNodeKind.Agent),
            ("pass", WorkflowNodeKind.Function),
            ("two", WorkflowNodeKind.Agent)));

        var checkpoint = await RunAndPickFirstCheckpointAsync(host);

        await host.SaveAsync(NodeChain("chain", ("one", WorkflowNodeKind.Agent), ("pass", WorkflowNodeKind.Function)));

        await AssertTheRecordedVersionResumesAndTheEditedOneDoesNotAsync(host, checkpoint);
    }

    [Fact]
    public async Task Changing_the_pattern_of_a_chain_makes_the_old_checkpoint_unusable_for_the_edited_version()
    {
        var host = new WorkflowTestHost("one", "two");

        await host.SaveAsync(AgentOnly("chain", "one", "two"));

        var checkpoint = await RunAndPickFirstCheckpointAsync(host);

        await host.SaveAsync(new WorkflowDefinition { Name = "chain", Kind = WorkflowKind.Concurrent, AgentNames = ["one", "two"] });

        await AssertTheRecordedVersionResumesAndTheEditedOneDoesNotAsync(host, checkpoint);
    }

    [Fact]
    public async Task Reordering_the_agents_of_a_concurrent_workflow_makes_the_old_checkpoint_unusable_for_the_edited_version_too()
    {
        // MEASURED, and not what the screen's own "order does not affect this pattern" hint would
        // suggest: the hint is about the run's result, the checkpoint is about the graph, and the
        // graph is built in list order. The console therefore warns about an order change in every
        // pattern, not only in a Sequential chain.
        var host = new WorkflowTestHost("one", "two");

        await host.SaveAsync(new WorkflowDefinition { Name = "chain", Kind = WorkflowKind.Concurrent, AgentNames = ["one", "two"] });

        var checkpoint = await RunAndPickFirstCheckpointAsync(host);

        await host.SaveAsync(new WorkflowDefinition { Name = "chain", Kind = WorkflowKind.Concurrent, AgentNames = ["two", "one"] });

        await AssertTheRecordedVersionResumesAndTheEditedOneDoesNotAsync(host, checkpoint);
    }

    [Fact]
    public async Task Adding_a_handoff_edge_makes_the_old_checkpoint_unusable_for_the_edited_version()
    {
        // The edge list is the graph of a Handoff workflow: the console warns before it saves a change of
        // an edge the same way it does for an agent added to a chain.
        var host = new WorkflowTestHost("one", "two");

        await host.SaveAsync(Handoff("chain", [Edge("one", "two")]));

        var checkpoint = await RunAndPickFirstCheckpointAsync(host, last: true);

        await host.SaveAsync(Handoff("chain", [Edge("one", "two"), Edge("two", "one")]));

        await AssertTheRecordedVersionResumesAndTheEditedOneDoesNotAsync(host, checkpoint);
    }

    [Fact]
    public async Task Replacing_the_star_with_the_same_edges_written_out_leaves_the_old_checkpoint_usable()
    {
        // An empty list is the star: the first agent hands off to every other agent. Writing those edges out is the
        // same graph, so a client that moves from "no list" to the explicit list does not strand a waiting run.
        var host = new WorkflowTestHost("one", "two");

        await host.SaveAsync(Handoff("chain", []));

        var checkpoint = await RunAndPickFirstCheckpointAsync(host, last: true);

        var written = await host.SaveAsync(Handoff("chain", [Edge("one", "two")]));

        (await ResumeFailureAsync(host, checkpoint)).ShouldBeNull();
        (await ResumeFailureAsync(host, checkpoint, written.Version)).ShouldBeNull();
    }

    [Fact]
    public async Task Changing_the_handoff_limit_or_the_reason_of_an_edge_leaves_the_old_checkpoint_usable()
    {
        var host = new WorkflowTestHost("one", "two");

        await host.SaveAsync(Handoff("chain", [Edge("one", "two")]));

        var checkpoint = await RunAndPickFirstCheckpointAsync(host, last: true);

        var edited = await host.SaveAsync(Handoff("chain", [new WorkflowHandoff { From = "one", To = "two", Reason = "A new reason." }]) with { MaxHandoffs = 3 });

        (await ResumeFailureAsync(host, checkpoint)).ShouldBeNull();
        (await ResumeFailureAsync(host, checkpoint, edited.Version)).ShouldBeNull();
    }

    [Fact]
    public async Task A_description_change_leaves_the_old_checkpoint_usable()
    {
        var host = new WorkflowTestHost("one", "two");

        await host.SaveAsync(AgentOnly("chain", "one", "two"));

        var checkpoint = await RunAndPickFirstCheckpointAsync(host);

        var edited = await host.SaveAsync(AgentOnly("chain", "one", "two") with { Description = "Reworded." });

        (await ResumeFailureAsync(host, checkpoint)).ShouldBeNull();
        (await ResumeFailureAsync(host, checkpoint, edited.Version)).ShouldBeNull();
    }

    [Fact]
    public async Task Re_pinning_an_agent_step_leaves_the_old_checkpoint_usable_for_either_version()
    {
        // Failure mode 15: the pin is a property of the wrapper, never of the executor id.
        // A pin that reached the id would strand every waiting run the moment an operator pinned.
        var host = new WorkflowTestHost("one", "two");

        await host.SaveAsync(NodeChain("chain", ("one", WorkflowNodeKind.Agent), ("two", WorkflowNodeKind.Agent)));

        var checkpoint = await RunAndPickFirstCheckpointAsync(host);

        var pinned = await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            Nodes =
            [
                new WorkflowNodeReference { Name = "one", Kind = WorkflowNodeKind.Agent, AgentVersion = 1 },
                new WorkflowNodeReference { Name = "two", Kind = WorkflowNodeKind.Agent },
            ],
        });

        var repinned = await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            Nodes =
            [
                new WorkflowNodeReference { Name = "one", Kind = WorkflowNodeKind.Agent, AgentVersion = 2 },
                new WorkflowNodeReference { Name = "two", Kind = WorkflowNodeKind.Agent, AgentVersion = 1 },
            ],
        });

        (await ResumeFailureAsync(host, checkpoint)).ShouldBeNull();
        (await ResumeFailureAsync(host, checkpoint, pinned.Version)).ShouldBeNull();
        (await ResumeFailureAsync(host, checkpoint, repinned.Version)).ShouldBeNull();
    }

    [Fact]
    public async Task A_resume_after_a_structural_edit_continues_the_chain_the_run_was_started_with()
    {
        var host = new WorkflowTestHost("one", "two", "three");

        await host.SaveAsync(AgentOnly("chain", "one", "two"));

        var checkpoint = await RunAndPickFirstCheckpointAsync(host);

        await host.SaveAsync(AgentOnly("chain", "one", "two", "three"));

        (await ResumeFailureAsync(host, checkpoint)).ShouldBeNull();

        var source = Guid.Parse(checkpoint.Split('|')[0]);
        var all = await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 });
        var resumedRoot = all.Single(run => run.Kind == RunKind.Workflow && run.Id != source);

        resumedRoot.Status.ShouldBe(RunStatus.Completed, resumedRoot.Error?.Message);
        resumedRoot.AgentVersion.ShouldBe(1, "the resume is stamped with the version it ran");

        // The agents the resumed run called are the two the old chain had.
        all.Where(run => run.ParentRunId == resumedRoot.Id)
            .Select(static run => run.AgentName)
            .ShouldNotContain("three", StringComparer.Ordinal);
    }

    /// <param name="host">The host.</param>
    /// <param name="last">
    /// Pick the last checkpoint of the run instead of the first. A Handoff run's earlier checkpoints cannot be resumed at
    /// all - the framework throws "Expected exactly one update for key 'SharedState'" for a star as well, which is not a
    /// structural edit - and a run that waits for input resumes from its last one, so that is the checkpoint a Handoff
    /// case uses.
    /// </param>
    private static async Task<string> RunAndPickFirstCheckpointAsync(WorkflowTestHost host, bool last = false)
    {
        await foreach (var _ in host.CreateRunner().RunStreamingAsync(new WorkflowRunRequest { WorkflowName = "chain", Message = "hello" }))
        {
        }

        var root = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(static run => run.Kind == RunKind.Workflow);

        root.Status.ShouldBe(RunStatus.Completed);

        var checkpoints = await host.CheckpointStore.ListByRunAsync(host.TenantContext.TenantId, root.Id);

        checkpoints.ShouldNotBeEmpty();

        return $"{root.Id}|{(last ? checkpoints[^1] : checkpoints[0]).CheckpointId}";
    }

    /// <summary>
    /// The two halves of a structural edit: the version the run recorded resumes, and the edited
    /// version - named on purpose - is the one the checkpoint does not fit.
    /// </summary>
    private static async Task AssertTheRecordedVersionResumesAndTheEditedOneDoesNotAsync(WorkflowTestHost host, string runAndCheckpoint)
    {
        var edited = (await host.DefinitionStore.GetAsync(host.TenantContext.TenantId, "chain"))!;

        edited.Version.ShouldBeGreaterThan(1);

        (await ResumeFailureAsync(host, runAndCheckpoint)).ShouldBeNull();
        (await ResumeFailureAsync(host, runAndCheckpoint, edited.Version)).ShouldNotBeNull().ShouldContain("structure differs", Case.Sensitive);
    }

    /// <summary>Resumes from the checkpoint; returns the error the run reports, or <see langword="null"/> when it resumed.</summary>
    private static async Task<string?> ResumeFailureAsync(WorkflowTestHost host, string runAndCheckpoint, int? definitionVersion = null)
    {
        var parts = runAndCheckpoint.Split('|');
        var events = new List<RunEvent>();

        try
        {
            await foreach (var runEvent in host.CreateRunner().ResumeStreamingAsync(new WorkflowResumeRequest
            {
                RunId = Guid.Parse(parts[0]),
                CheckpointId = parts[1],
                DefinitionVersion = definitionVersion,
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

    private static WorkflowHandoff Edge(string from, string to) => new() { From = from, To = to };

    private static WorkflowDefinition Handoff(string name, IReadOnlyList<WorkflowHandoff> edges)
        => new() { Name = name, Kind = WorkflowKind.Handoff, AgentNames = ["one", "two"], Handoffs = edges };

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
