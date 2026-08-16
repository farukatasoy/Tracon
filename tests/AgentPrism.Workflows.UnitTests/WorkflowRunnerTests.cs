using AgentPrism.Workflows.UnitTests.Fakes;

namespace AgentPrism.Workflows.UnitTests;

/// <summary>
/// Behavior of the runner visible to a third party: run records, the tree,
/// checkpoints, and limits.
/// </summary>
public sealed class WorkflowRunnerTests
{
    [Fact]
    public async Task Sequential_run_produces_a_tree()
    {
        var host = new WorkflowTestHost("writer", "editor", "reviewer");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["writer", "editor", "reviewer"],
        });

        var runner = host.CreateRunner();
        var events = await Collect(runner, "chain", "hello");

        events.ShouldNotBeEmpty();

        // Root row: one workflow run.
        var runs = await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 });
        var root = runs.Single(run => run.Kind == RunKind.Workflow);

        root.WorkflowName.ShouldBe("chain");
        root.AgentName.ShouldBe("chain");
        root.Status.ShouldBe(RunStatus.Completed);
        root.Depth.ShouldBe(0);

        // Three agent rows, all under the root. This is what lets the
        // waterfall view be drawn correctly.
        var children = runs.Where(run => run.Kind == RunKind.Agent).ToList();

        children.Count.ShouldBe(3);
        children.ShouldAllBe(run => run.ParentRunId == root.Id);
        children.ShouldAllBe(run => run.RootRunId == root.Id);
        children.ShouldAllBe(run => run.Depth == 1);
        children.Select(run => run.AgentName).Order(StringComparer.Ordinal)
            .ShouldBe(["editor", "reviewer", "writer"]);
    }

    [Fact]
    public async Task Chain_output_flows_to_the_next_agent()
    {
        var host = new WorkflowTestHost("one", "two");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["one", "two"],
        });

        var runner = host.CreateRunner();
        var events = await Collect(runner, "chain", "input");

        // EchoAgent tags the incoming text; if the second link did not see the
        // first one's output, the text would not be nested.
        var output = events.Single(runEvent => runEvent.Type == RunEventType.WorkflowOutput);

        output.Text.ShouldNotBeNull();
        output.Text!.ShouldContain("[two][one]input", Case.Sensitive);
    }

    [Fact]
    public async Task Checkpoints_are_written()
    {
        var host = new WorkflowTestHost("writer", "editor");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["writer", "editor"],
        });

        var runner = host.CreateRunner();
        await Collect(runner, "chain", "hello");

        var runs = await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 });
        var root = runs.Single(run => run.Kind == RunKind.Workflow);

        var checkpoints = await host.CheckpointStore.ListByRunAsync(host.TenantContext.TenantId, root.Id);

        checkpoints.ShouldNotBeEmpty();
        checkpoints.ShouldAllBe(record => record.RunId == root.Id);

        // The list is metadata; the state payload is deliberately not read.
        checkpoints.ShouldAllBe(record => WorkflowCheckpointState.IsOmitted(record.State));
    }

    [Fact]
    public async Task No_checkpoint_is_written_while_checkpointing_is_disabled()
    {
        var host = new WorkflowTestHost("writer", "editor");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["writer", "editor"],
        });

        var runner = host.CreateRunner(options => options.EnableCheckpointing = false);
        await Collect(runner, "chain", "hello");

        var runs = await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 });
        var root = runs.Single(run => run.Kind == RunKind.Workflow);

        (await host.CheckpointStore.ListByRunAsync(host.TenantContext.TenantId, root.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Resuming_from_a_checkpoint_works()
    {
        var host = new WorkflowTestHost("writer", "editor");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["writer", "editor"],
        });

        var runner = host.CreateRunner();
        await Collect(runner, "chain", "hello");

        var first = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(run => run.Kind == RunKind.Workflow);

        var resumed = new List<RunEvent>();

        await foreach (var runEvent in runner.ResumeStreamingAsync(new WorkflowResumeRequest { RunId = first.Id }))
        {
            resumed.Add(runEvent);
        }

        resumed.ShouldNotBeEmpty();

        // Resuming opens a NEW run record: reopening the same row would break
        // the append-only rule of the event stream.
        var workflowRuns = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Where(run => run.Kind == RunKind.Workflow)
            .ToList();

        workflowRuns.Count.ShouldBe(2);
        workflowRuns.ShouldAllBe(run => run.WorkflowName == "chain");
        workflowRuns.Select(run => run.SessionId).Distinct(StringComparer.Ordinal).Count().ShouldBe(1);
    }

    [Fact]
    public async Task Another_tenants_run_cannot_be_resumed()
    {
        var host = new WorkflowTestHost("writer", "editor");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["writer", "editor"],
        });

        var runner = host.CreateRunner();
        await Collect(runner, "chain", "hello");

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(record => record.Kind == RunKind.Workflow);

        host.TenantContext.TenantId = "other-tenant";

        var exception = await Should.ThrowAsync<AgentPrismException>(async () =>
        {
            await foreach (var _ in runner.ResumeStreamingAsync(new WorkflowResumeRequest { RunId = run.Id }))
            {
                // The stream must never start.
            }
        });

        // The message does not say "unauthorized": whether another tenant's
        // run EXISTS at all must not be leaked either.
        exception.Message.ShouldContain("There is no run", Case.Sensitive);
        exception.Message.ShouldNotContain("permission", Case.Sensitive);
    }

    [Fact]
    public async Task Unknown_workflow_fails_the_run()
    {
        var host = new WorkflowTestHost("writer");
        var runner = host.CreateRunner();

        var events = await Collect(runner, "missing", "hello");

        events[^1].Type.ShouldBe(RunEventType.RunFailed);

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(record => record.Kind == RunKind.Workflow);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error!.Message.ShouldContain("There is no workflow named 'missing'", Case.Sensitive);
    }

    [Fact]
    public async Task Super_step_limit_stops_the_run()
    {
        var host = new WorkflowTestHost("writer", "editor", "reviewer");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["writer", "editor", "reviewer"],
        });

        // A sequential chain needs four super-steps (three agents + the
        // output collector); with the limit set to one it must be cut off on
        // the second step.
        var runner = host.CreateRunner(options => options.MaxSuperSteps = 1);
        var events = await Collect(runner, "chain", "hello");

        events[^1].Type.ShouldBe(RunEventType.RunFailed);

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(record => record.Kind == RunKind.Workflow);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error!.Message.ShouldContain("exceeded the", Case.Sensitive);
        run.Error!.Message.ShouldContain("super-step limit", Case.Sensitive);
    }

    [Fact]
    public async Task Run_is_rejected_while_the_engine_is_disabled()
    {
        var host = new WorkflowTestHost("writer");
        var runner = host.CreateRunner(options => options.Enabled = false);

        await Should.ThrowAsync<AgentPrismException>(async () =>
        {
            await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest { WorkflowName = "chain" }))
            {
                // The stream must never start.
            }
        });
    }

    [Fact]
    public async Task Invalid_session_id_is_rejected()
    {
        var host = new WorkflowTestHost("writer");
        var runner = host.CreateRunner();

        // The session id groups checkpoints and comes from the client.
        // Using it without validation would mean access to another run's state.
        await Should.ThrowAsync<AgentPrismException>(async () =>
        {
            await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest
            {
                WorkflowName = "chain",
                SessionId = "../../etc/passwd",
            }))
            {
                // The stream must never start.
            }
        });
    }

    [Fact]
    public async Task Workflow_defined_in_code_takes_priority_over_the_one_in_the_database()
    {
        var host = new WorkflowTestHost("writer", "editor");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["writer", "editor"],
        });

        var runner = host.CreateRunner(
            configure: null,
            services: null,
            new CodeWorkflowRegistration(
                "chain",
                "Defined in code.",
                _ => Microsoft.Agents.AI.Workflows.AgentWorkflowBuilder.BuildSequential("chain", [])));

        var descriptor = (await runner.GetAsync("chain"))!;

        descriptor.Origin.ShouldBe(AgentDefinitionOrigin.Code);
        descriptor.Description.ShouldBe("Defined in code.");
    }

    private static async Task<List<RunEvent>> Collect(WorkflowRunner runner, string name, string message)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in runner.RunStreamingAsync(new WorkflowRunRequest
        {
            WorkflowName = name,
            Message = message,
        }))
        {
            events.Add(runEvent);
        }

        return events;
    }
}
