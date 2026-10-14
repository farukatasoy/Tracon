using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// The root row of a stored workflow's run carries the version of the definition it was compiled
/// from (phase 200, failure mode 13).
/// </summary>
/// <remarks>
/// <para>
/// The stamp is what a resume or a response reads to compile the SAME definition, so it must
/// name the version the graph was really built from. That holds only if the definition is read
/// once and the stamp and the graph both come from that read: two reads can see two versions of
/// a workflow being edited.
/// </para>
/// <para>
/// The row opens before the graph is built (the run id has been announced by then), so a read
/// that finds nothing or fails still leaves a row - closed <see cref="RunStatus.Failed"/>.
/// </para>
/// </remarks>
public sealed class WorkflowRunStampTests
{
    [Fact]
    public async Task A_run_is_stamped_with_the_version_it_was_compiled_from()
    {
        var host = new WorkflowTestHost("one", "two");

        await host.SaveAsync(Chain("one"));
        await host.SaveAsync(Chain("one", "two"));

        await RunAsync(host.CreateRunner());

        var root = await RootAsync(host);

        root.AgentVersion.ShouldBe(2, "the current version when the run started");
        root.Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task A_run_reads_the_definition_once_and_builds_its_graph_and_stamp_from_that_read()
    {
        var host = new WorkflowTestHost("one", "two");
        var probe = new ProbeWorkflowDefinitionStore(host.DefinitionStore);

        await host.SaveAsync(Chain("one", "two"));

        await RunAsync(host.CreateRunnerOver(probe));

        probe.Gets.ShouldBe(1, "one read feeds the stamp, the graph, the parameter schema and the run timeout");
        probe.VersionGets.ShouldBe(0);
        (await RootAsync(host)).AgentVersion.ShouldBe(1);
    }

    [Fact]
    public async Task A_run_whose_stamp_names_an_older_version_reads_the_current_row_then_that_version()
    {
        var host = new WorkflowTestHost("one", "two");
        var probe = new ProbeWorkflowDefinitionStore(host.DefinitionStore);

        await host.SaveAsync(Chain("one"));

        var runner = host.CreateRunnerOver(probe);
        await RunAsync(runner);

        var source = await RootAsync(host);
        var checkpoint = (await host.CheckpointStore.ListByRunAsync(host.TenantContext.TenantId, source.Id))[0];

        await host.SaveAsync(Chain("one", "two"));

        await foreach (var _ in runner.ResumeStreamingAsync(new WorkflowResumeRequest { RunId = source.Id, CheckpointId = checkpoint.CheckpointId }))
        {
            // Drained.
        }

        // The schema read and the graph read are separate callers of the same immutable version.
        probe.VersionGets.ShouldBeGreaterThanOrEqualTo(1);

        var resumed = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(run => run.Kind == RunKind.Workflow && run.Id != source.Id);

        resumed.AgentVersion.ShouldBe(1);
    }

    [Fact]
    public async Task A_workflow_defined_in_code_carries_no_stamp()
    {
        var host = new WorkflowTestHost();
        var runner = host.CreateRunner(configure: null, services: null, ApprovalWorkflow.Registration());

        await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest { WorkflowName = "approval-flow", Message = "go" }))
        {
            // Drained.
        }

        var root = await RootAsync(host);

        root.AgentVersion.ShouldBeNull("a code workflow has no versions to name");
        root.Status.ShouldBe(RunStatus.AwaitingInput);
    }

    [Fact]
    public async Task A_workflow_that_does_not_exist_still_opens_its_row_and_closes_it_failed()
    {
        var host = new WorkflowTestHost("one");
        var runner = host.CreateRunner();
        var events = new List<RunEvent>();

        await foreach (var runEvent in runner.RunStreamingAsync(new WorkflowRunRequest { WorkflowName = "missing", Message = "go" }))
        {
            events.Add(runEvent);
        }

        // The run id the endpoint announces must point at a run.
        events.First().Type.ShouldBe(RunEventType.RunStarted);

        var root = await RootAsync(host);

        root.Status.ShouldBe(RunStatus.Failed);
        root.AgentVersion.ShouldBeNull();
        root.Error.ShouldNotBeNull().Message.ShouldContain("There is no workflow named 'missing'", Case.Sensitive);
    }

    [Fact]
    public async Task A_definition_read_that_throws_still_opens_its_row_and_closes_it_failed_without_a_stamp()
    {
        var host = new WorkflowTestHost("one");
        var probe = new ProbeWorkflowDefinitionStore(host.DefinitionStore)
        {
            GetFailure = new InvalidOperationException("the database is down"),
        };

        await host.SaveAsync(Chain("one"));

        var events = new List<RunEvent>();

        await foreach (var runEvent in host.CreateRunnerOver(probe).RunStreamingAsync(new WorkflowRunRequest { WorkflowName = "chain", Message = "go" }))
        {
            events.Add(runEvent);
        }

        events.First().Type.ShouldBe(RunEventType.RunStarted);
        events.Last().Type.ShouldBe(RunEventType.RunFailed);

        var root = await RootAsync(host);

        root.Status.ShouldBe(RunStatus.Failed);
        root.AgentVersion.ShouldBeNull("no definition was read, so no version is named");
    }

    private static WorkflowDefinition Chain(params string[] agents)
        => new() { Name = "chain", Kind = WorkflowKind.Sequential, AgentNames = agents };

    private static async Task RunAsync(WorkflowRunner runner)
    {
        await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest { WorkflowName = "chain", Message = "go" }))
        {
            // Drained.
        }
    }

    private static async Task<RunRecord> RootAsync(WorkflowTestHost host)
        => (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(static run => run.Kind == RunKind.Workflow && run.ParentRunId is null);
}
