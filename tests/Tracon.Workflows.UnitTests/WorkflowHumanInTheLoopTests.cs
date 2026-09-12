using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// The full cycle of a run waiting on human input: wait, list, respond, finish.
/// </summary>
public sealed class WorkflowHumanInTheLoopTests
{
    [Fact]
    public async Task Pending_request_makes_the_run_AwaitingInput()
    {
        var host = new WorkflowTestHost();
        var runner = host.CreateRunner(configure: null, services: null, ApprovalWorkflow.Registration());

        var events = await Collect(runner, "approval-flow", "publish the report");

        // The flow's last event must report waiting, not an error.
        events[^1].Type.ShouldBe(RunEventType.RunAwaitingInput);
        events.ShouldContain(runEvent => runEvent.Type == RunEventType.WorkflowRequest);

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();

        run.Status.ShouldBe(RunStatus.AwaitingInput);
        run.Error.ShouldBeNull();
    }

    [Fact]
    public async Task Pending_request_writes_a_checkpoint()
    {
        var host = new WorkflowTestHost();
        var runner = host.CreateRunner(configure: null, services: null, ApprovalWorkflow.Registration());

        await Collect(runner, "approval-flow", "publish the report");

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();

        // Without a checkpoint there would be nowhere to resume execution
        // state from when a response comes in.
        var checkpoints = await host.CheckpointStore.ListByRunAsync(host.TenantContext.TenantId, run.Id);

        checkpoints.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Pending_request_is_listed_and_carries_its_prompt()
    {
        var host = new WorkflowTestHost();
        var runner = host.CreateRunner(configure: null, services: null, ApprovalWorkflow.Registration());

        await Collect(runner, "approval-flow", "publish the report");

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();
        var pending = await runner.ListPendingRequestsAsync(run.Id);

        var request = pending.ShouldHaveSingleItem();

        request.RunId.ShouldBe(run.Id);
        request.PortId.ShouldBe(ApprovalWorkflow.PortId);
        request.RequestId.ShouldNotBeNullOrWhiteSpace();

        // The response type is bool, so the UI must ask a yes/no question.
        request.Form.ShouldBe(WorkflowRequestForm.Boolean);
        request.Prompt.ShouldNotBeNull();
        request.Prompt!.ShouldContain("publish the report", Case.Sensitive);
    }

    [Fact]
    public async Task Once_a_response_is_given_the_run_completes()
    {
        var host = new WorkflowTestHost();
        var runner = host.CreateRunner(configure: null, services: null, ApprovalWorkflow.Registration());

        await Collect(runner, "approval-flow", "publish the report");

        var first = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();
        var pending = (await runner.ListPendingRequestsAsync(first.Id)).Single();

        var resumed = await CollectResponse(runner, first.Id, pending.RequestId, approved: true);

        // Resuming opens a NEW row; the old row is never mutated retroactively.
        var runs = await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 });
        var second = runs.Single(run => run.Id != first.Id);

        second.Status.ShouldBe(RunStatus.Completed);
        second.WorkflowName.ShouldBe("approval-flow");

        var output = resumed.Single(runEvent => runEvent.Type == RunEventType.WorkflowOutput);

        output.Text.ShouldBe("approved");
    }

    [Fact]
    public async Task A_rejection_response_also_flows_through_execution()
    {
        var host = new WorkflowTestHost();
        var runner = host.CreateRunner(configure: null, services: null, ApprovalWorkflow.Registration());

        await Collect(runner, "approval-flow", "publish the report");

        var first = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();
        var pending = (await runner.ListPendingRequestsAsync(first.Id)).Single();

        var resumed = await CollectResponse(runner, first.Id, pending.RequestId, approved: false);

        resumed.Single(runEvent => runEvent.Type == RunEventType.WorkflowOutput).Text.ShouldBe("rejected");
    }

    [Fact]
    public async Task Answered_run_no_longer_shows_a_pending_request()
    {
        var host = new WorkflowTestHost();
        var runner = host.CreateRunner(configure: null, services: null, ApprovalWorkflow.Registration());

        await Collect(runner, "approval-flow", "publish the report");

        var first = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();
        var pending = (await runner.ListPendingRequestsAsync(first.Id)).Single();

        await CollectResponse(runner, first.Id, pending.RequestId, approved: true);

        // The second row is completed; it has no pending request.
        var second = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 }))
            .Single(run => run.Id != first.Id);

        (await runner.ListPendingRequestsAsync(second.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Unknown_request_id_is_rejected()
    {
        var host = new WorkflowTestHost();
        var runner = host.CreateRunner(configure: null, services: null, ApprovalWorkflow.Registration());

        await Collect(runner, "approval-flow", "publish the report");

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();

        // If this resumed silently, the run would still end up waiting and the
        // user would never see why their response had no effect.
        var error = await Should.ThrowAsync<TraconException>(
            async () => await CollectResponse(runner, run.Id, "no-such-request", approved: true));

        error.Message.ShouldContain("no-such-request", Case.Sensitive);
    }

    [Fact]
    public async Task A_run_that_is_not_waiting_cannot_be_answered()
    {
        var host = new WorkflowTestHost("writer");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["writer"],
        });

        var runner = host.CreateRunner();

        await Collect(runner, "chain", "input");

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();

        var error = await Should.ThrowAsync<TraconException>(
            async () => await CollectResponse(runner, run.Id, "any", approved: true));

        error.Message.ShouldContain("is not awaiting human input", Case.Sensitive);
    }

    [Fact]
    public async Task Another_tenants_run_cannot_be_answered()
    {
        var host = new WorkflowTestHost();
        var runner = host.CreateRunner(configure: null, services: null, ApprovalWorkflow.Registration());

        await Collect(runner, "approval-flow", "publish the report");

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();
        var pending = (await runner.ListPendingRequestsAsync(run.Id)).Single();

        host.TenantContext.TenantId = "other-tenant";

        // Not even "unauthorized" is said: whether another tenant's run exists
        // at all must not be leaked either.
        var error = await Should.ThrowAsync<TraconException>(
            async () => await CollectResponse(runner, run.Id, pending.RequestId, approved: true));

        error.Message.ShouldContain("There is no run", Case.Sensitive);
    }

    [Fact]
    public async Task Waiting_fails_when_checkpointing_is_disabled()
    {
        var host = new WorkflowTestHost();

        var runner = host.CreateRunner(
            options => options.EnableCheckpointing = false,
            services: null,
            ApprovalWorkflow.Registration());

        var events = await Collect(runner, "approval-flow", "publish the report");

        // Reporting a wait that can never be resumed as "waiting" would leave
        // the user waiting for a continuation that never comes.
        events[^1].Type.ShouldBe(RunEventType.RunFailed);

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error!.Message.ShouldContain("EnableCheckpointing", Case.Sensitive);
    }

    [Fact]
    public async Task Checkpoints_of_a_pending_run_SURVIVE_even_with_cleanup_enabled()
    {
        var host = new WorkflowTestHost();

        var runner = host.CreateRunner(
            options => options.KeepCheckpointsAfterCompletion = false,
            services: null,
            ApprovalWorkflow.Registration());

        await Collect(runner, "approval-flow", "publish the report");

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();

        // If they were deleted, there would be nowhere left to resume to.
        (await host.CheckpointStore.ListByRunAsync(host.TenantContext.TenantId, run.Id)).ShouldNotBeEmpty();
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

    private static async Task<List<RunEvent>> CollectResponse(
        WorkflowRunner runner,
        Guid runId,
        string requestId,
        bool approved)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in runner.RespondStreamingAsync(new WorkflowRespondRequest
        {
            RunId = runId,
            RequestId = requestId,
            Approved = approved,
        }))
        {
            events.Add(runEvent);
        }

        return events;
    }
}
