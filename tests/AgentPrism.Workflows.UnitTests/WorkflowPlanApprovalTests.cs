using AgentPrism.Workflows.UnitTests.Fakes;

namespace AgentPrism.Workflows.UnitTests;

/// <summary>
/// Plan approval for the <c>Magentic</c> pattern.
/// </summary>
/// <remarks>
/// In Phase 15 this path was <em>disabled</em>: MAF published a plan approval
/// request at the end of the first super-step and execution stayed waiting
/// forever, with no way to respond. When Phase 16 introduced the
/// human-in-the-loop flow, approval became possible again; the tests below
/// prove it actually works.
/// </remarks>
public sealed class WorkflowPlanApprovalTests
{
    [Fact]
    public void Plan_approval_is_only_accepted_for_the_Magentic_pattern()
    {
        var message = WorkflowDefinitionValidator.Validate(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["one", "two"],
            RequirePlanApproval = true,
        });

        message.ShouldNotBeNull();
        message!.ShouldContain("requirePlanApproval", Case.Sensitive);
    }

    [Fact]
    public void Magentic_accepts_plan_approval()
        => WorkflowDefinitionValidator.Validate(new WorkflowDefinition
        {
            Name = "magentic",
            Kind = WorkflowKind.Magentic,
            AgentNames = ["worker"],
            ManagerAgentName = "manager",
            RequirePlanApproval = true,
        }).ShouldBeNull();

    [Fact]
    public async Task Run_waits_for_a_human_while_plan_approval_is_on()
    {
        var host = new WorkflowTestHost("manager", "worker");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "magentic",
            Kind = WorkflowKind.Magentic,
            AgentNames = ["worker"],
            ManagerAgentName = "manager",
            MaxIterations = 2,
            RequirePlanApproval = true,
        });

        var runner = host.CreateRunner();
        var events = new List<RunEvent>();

        await foreach (var runEvent in runner.RunStreamingAsync(new WorkflowRunRequest
        {
            WorkflowName = "magentic",
            Message = "prepare the report",
        }))
        {
            events.Add(runEvent);
        }

        events[^1].Type.ShouldBe(RunEventType.RunAwaitingInput);

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();

        run.Status.ShouldBe(RunStatus.AwaitingInput);

        // The UI asks for plan approval with its own card: approve, or send
        // back with revision text.
        var pending = (await runner.ListPendingRequestsAsync(run.Id)).ShouldHaveSingleItem();

        pending.Form.ShouldBe(WorkflowRequestForm.PlanReview);
        pending.Prompt.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Run_does_not_wait_while_plan_approval_is_off()
    {
        var host = new WorkflowTestHost("manager", "worker");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "magentic",
            Kind = WorkflowKind.Magentic,
            AgentNames = ["worker"],
            ManagerAgentName = "manager",
            MaxIterations = 2,
        });

        var runner = host.CreateRunner();

        await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest
        {
            WorkflowName = "magentic",
            Message = "prepare the report",
        }))
        {
            // The events are not the point of this test.
        }

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();

        run.Status.ShouldNotBe(RunStatus.AwaitingInput);
    }

    [Fact]
    public async Task Execution_continues_once_the_plan_is_approved()
    {
        var host = new WorkflowTestHost("manager", "worker");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "magentic",
            Kind = WorkflowKind.Magentic,
            AgentNames = ["worker"],
            ManagerAgentName = "manager",
            MaxIterations = 2,
            RequirePlanApproval = true,
        });

        var runner = host.CreateRunner();

        await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest
        {
            WorkflowName = "magentic",
            Message = "prepare the report",
        }))
        {
            // The first round only builds the plan.
        }

        var first = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();
        var pending = (await runner.ListPendingRequestsAsync(first.Id)).Single();

        var resumed = new List<RunEvent>();

        await foreach (var runEvent in runner.RespondStreamingAsync(new WorkflowRespondRequest
        {
            RunId = first.Id,
            RequestId = pending.RequestId,
            Approved = true,
        }))
        {
            resumed.Add(runEvent);
        }

        // After approval the graph genuinely advances: the manager runs again.
        resumed.ShouldContain(runEvent => runEvent.Type == RunEventType.ExecutorInvoked);

        // Resuming opens a NEW row; the old row is never mutated retroactively.
        var second = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 }))
            .Single(run => run.Id != first.Id);

        second.WorkflowName.ShouldBe("magentic");

        // The second round asking for approval again is EXPECTED behavior: the
        // fake manager does not produce a meaningful plan, so MAF re-plans and
        // resubmits it for approval. What matters is that the loop does not
        // jam — every round produces a request that can be answered.
        if (second.Status == RunStatus.AwaitingInput)
        {
            (await runner.ListPendingRequestsAsync(second.Id)).ShouldNotBeEmpty();
        }
    }

    [Fact]
    public async Task Rejecting_a_plan_requires_revision_text()
    {
        var host = new WorkflowTestHost("manager", "worker");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "magentic",
            Kind = WorkflowKind.Magentic,
            AgentNames = ["worker"],
            ManagerAgentName = "manager",
            MaxIterations = 2,
            RequirePlanApproval = true,
        });

        var runner = host.CreateRunner();

        await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest
        {
            WorkflowName = "magentic",
            Message = "prepare the report",
        }))
        {
            // The first round only builds the plan.
        }

        var first = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();
        var pending = (await runner.ListPendingRequestsAsync(first.Id)).Single();

        var resumed = new List<RunEvent>();

        await foreach (var runEvent in runner.RespondStreamingAsync(new WorkflowRespondRequest
        {
            RunId = first.Id,
            RequestId = pending.RequestId,
            Approved = false,
        }))
        {
            resumed.Add(runEvent);
        }

        // Without revision text the manager agent has nothing to rebuild the
        // plan from; silently approving would be misleading.
        var failure = resumed[^1];

        failure.Type.ShouldBe(RunEventType.RunFailed);
        failure.Text.ShouldNotBeNull();
        failure.Text!.ShouldContain("revision text", Case.Sensitive);
    }
}
