using System.Net;
using System.Net.Http.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.AgentStepHost;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.ToolApprovalWorkflowHost;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The ready-made patterns beyond Sequential wait for a tool approval too, and an agent below a step still
/// cannot.
/// </summary>
/// <remarks>
/// Each pattern hosts its agents with a different executor, so a case that passes for Sequential does not prove
/// the others (MAF-KAPSAMA, row 38: the behaviour was read from decompiled code and never measured).
/// </remarks>
public sealed class WorkflowToolApprovalPatternTests
{
    private const string EditorModel = "step-model-b";
    private const string RefundTool = "refund_order";

    // Microsoft Agent Framework's background task tools.
    private const string StartTask = "background_agents_start_task";
    private const string WaitForCompletion = "background_agents_wait_for_first_completion";
    private const string GetResults = "background_agents_get_task_results";

    [Fact]
    public async Task Two_agents_of_a_concurrent_workflow_ask_at_once_and_each_request_is_answered_on_its_own()
    {
        var cancels = new int[1];
        var refunds = new int[1];

        await using var host = await StartTwoAgentsAsync(cancels, refunds);

        await SaveAsync(host, "both-ask", WorkflowKind.Concurrent, [Agent, "editor"]);

        var waiting = await RunToApprovalAsync(host, "both-ask");

        waiting.Run.Status.ShouldBe(RunStatus.AwaitingInput, waiting.Run.Error?.Message);
        waiting.Requests.Count.ShouldBe(2, "each agent's approval is a request of its own");
        waiting.Requests.ShouldAllBe(static request => string.Equals(request.Form, "ToolApproval", StringComparison.Ordinal));
        waiting.Requests.ShouldContain(static request => request.Prompt.Contains(Tool, StringComparison.Ordinal));
        waiting.Requests.ShouldContain(static request => request.Prompt.Contains(RefundTool, StringComparison.Ordinal));

        // The first answer approves one tool; the other request stays open on the run that carries it on.
        var cancelRequest = waiting.Requests.Single(static request => request.Prompt.Contains(Tool, StringComparison.Ordinal));
        using var first = await RespondAsync(host, waiting.Run.Id, cancelRequest.Id, new { approved = true });

        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());

        var afterFirst = await ReadWaitingAsync(host, await ReadRunIdAsync(first));

        cancels[0].ShouldBe(1);
        refunds[0].ShouldBe(0, "the second request was not answered");
        afterFirst.Run.Status.ShouldBe(RunStatus.AwaitingInput, afterFirst.Run.Error?.Message);

        var refundRequest = afterFirst.Requests.ShouldHaveSingleItem();

        refundRequest.Prompt.ShouldContain(RefundTool, Case.Sensitive);

        using var second = await RespondAsync(host, afterFirst.Run.Id, refundRequest.Id, new { approved = false, text = "already refunded" });

        second.StatusCode.ShouldBe(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());

        var finished = (await ReadWaitingAsync(host, await ReadRunIdAsync(second))).Run;

        finished.Status.ShouldBe(RunStatus.Completed, finished.Error?.Message);
        cancels[0].ShouldBe(1);
        refunds[0].ShouldBe(0, "the refused tool must not run");
    }

    [Fact]
    public async Task An_agent_a_handoff_reached_waits_for_an_approval_and_goes_on_after_the_answer()
    {
        var cancels = new int[1];

        var provider = new FakeModelProvider(Provider)
            .WithModel(new ModelDescriptor { Name = Model })
            .WithModel(new ModelDescriptor { Name = EditorModel })
            .ForModel(Model, static cfg => cfg.CallsTool("handoff_to_1").RespondsWith("triage answered"))
            .ForModel(EditorModel, static cfg => cfg.CallsTool(Tool, new { orderId = "ORD-7" }).EchoesLastToolResult());

        await using var host = await TraconTestHost.StartAsync(builder =>
        {
            builder.AddModelProvider(provider);
            builder.AddAgent(new AgentDefinition
            {
                Name = "triage",
                Description = "Routes the question.",
                Instructions = "You are the triage.",
                Model = new ModelBinding { Provider = Provider, Model = Model },
                Origin = AgentDefinitionOrigin.Code,
            });
            builder.AddAgent(new AgentDefinition
            {
                Name = "expert",
                Description = "Cancels orders.",
                Instructions = "You are the expert.",
                Model = new ModelBinding { Provider = Provider, Model = EditorModel },
                ToolNames = [Tool],
                Origin = AgentDefinitionOrigin.Code,
            });
            AddApprovalTool(builder, Tool, cancels);
            builder.UseWorkflows();
        });

        await SaveAsync(host, "help-desk", WorkflowKind.Handoff, ["triage", "expert"]);

        var waiting = await RunToApprovalAsync(host, "help-desk");

        waiting.Run.Status.ShouldBe(RunStatus.AwaitingInput, waiting.Run.Error?.Message);
        waiting.Form.ShouldBe("ToolApproval");
        waiting.Prompt.ShouldContain(Tool, Case.Sensitive);
        cancels[0].ShouldBe(0);

        using var answer = await RespondAsync(host, waiting, new { approved = true });

        answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());

        var resumed = (await ReadWaitingAsync(host, await ReadRunIdAsync(answer))).Run;

        resumed.Status.ShouldBe(RunStatus.Completed, resumed.Error?.Message);
        cancels[0].ShouldBe(1);
    }

    [Fact]
    public async Task A_handoff_run_that_waits_for_an_approval_is_not_counted_twice_when_the_answer_resumes_it()
    {
        // The run hands off once and then waits for the approval of the expert's tool; the answer opens a new
        // run that carries it on. With a limit of ONE handoff the resumed run completes only if it does not count the
        // handoff the first run already made - a resume that replays the handoff call and counts it would stop here.
        // It does not say whether a count is carried over: the next test does.
        var cancels = new int[1];

        var provider = new FakeModelProvider(Provider)
            .WithModel(new ModelDescriptor { Name = Model })
            .WithModel(new ModelDescriptor { Name = EditorModel })
            .ForModel(Model, static cfg => cfg.CallsTool("handoff_to_1").RespondsWith("triage answered"))
            .ForModel(EditorModel, static cfg => cfg.CallsTool(Tool, new { orderId = "ORD-7" }).EchoesLastToolResult());

        await using var host = await StartTriageExpertAsync(provider, cancels);

        await SaveTriageAsync(host, [new WorkflowHandoff { From = "triage", To = "expert" }]);

        var waiting = await RunToApprovalAsync(host, "help-desk");

        waiting.Run.Status.ShouldBe(RunStatus.AwaitingInput, waiting.Run.Error?.Message);

        using var answer = await RespondAsync(host, waiting, new { approved = true });

        answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());

        var resumed = (await ReadWaitingAsync(host, await ReadRunIdAsync(answer))).Run;

        resumed.Status.ShouldBe(RunStatus.Completed, resumed.Error?.Message);
        cancels[0].ShouldBe(1);
    }

    [Fact]
    public async Task A_run_that_resumes_after_an_approval_counts_its_own_handoffs_from_zero()
    {
        // One handoff before the pause and one after it, with a limit of ONE: the resumed run is a new
        // run with a counter of its own, so the handoff back to the triage agent is its first. A count carried over the
        // pause would make it the second and fail the run. The cost of the choice: a loop that stops for an approval at
        // every hop starts again at each answer, and only the run budget bounds it.
        var cancels = new int[1];

        var provider = new FakeModelProvider(Provider)
            .WithModel(new ModelDescriptor { Name = Model })
            .WithModel(new ModelDescriptor { Name = EditorModel })
            .ForModel(Model, static cfg => cfg.CallsTool("handoff_to_1").RespondsWith("triage closes the case"))
            .ForModel(EditorModel, static cfg => cfg.CallsTool(Tool, new { orderId = "ORD-7" }).CallsTool("handoff_to_1"));

        await using var host = await StartTriageExpertAsync(provider, cancels);

        await SaveTriageAsync(
            host,
            [new WorkflowHandoff { From = "triage", To = "expert" }, new WorkflowHandoff { From = "expert", To = "triage" }]);

        var waiting = await RunToApprovalAsync(host, "help-desk");

        waiting.Run.Status.ShouldBe(RunStatus.AwaitingInput, waiting.Run.Error?.Message);

        using var answer = await RespondAsync(host, waiting, new { approved = true });

        answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());

        var resumed = (await ReadWaitingAsync(host, await ReadRunIdAsync(answer))).Run;

        resumed.Status.ShouldBe(RunStatus.Completed, resumed.Error?.Message);
        cancels[0].ShouldBe(1);
    }

    private static Task<TraconTestHost> StartTriageExpertAsync(FakeModelProvider provider, int[] cancels)
        => TraconTestHost.StartAsync(builder =>
        {
            builder.AddModelProvider(provider);
            builder.AddAgent(new AgentDefinition
            {
                Name = "triage",
                Description = "Routes the question.",
                Instructions = "You are the triage.",
                Model = new ModelBinding { Provider = Provider, Model = Model },
                Origin = AgentDefinitionOrigin.Code,
            });
            builder.AddAgent(new AgentDefinition
            {
                Name = "expert",
                Description = "Cancels orders.",
                Instructions = "You are the expert.",
                Model = new ModelBinding { Provider = Provider, Model = EditorModel },
                ToolNames = [Tool],
                Origin = AgentDefinitionOrigin.Code,
            });
            AddApprovalTool(builder, Tool, cancels);
            builder.UseWorkflows();
        });

    private static async Task SaveTriageAsync(TraconTestHost host, IReadOnlyList<WorkflowHandoff> edges)
    {
        using var saved = await host.Client.PutAsJsonAsync(
            "/tracon/api/workflows/help-desk",
            new WorkflowSaveRequest
            {
                Kind = WorkflowKind.Handoff,
                AgentNames = ["triage", "expert"],
                Handoffs = edges,
                MaxHandoffs = 1,
            });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_sub_agent_the_step_calls_is_still_refused_and_the_workflow_does_not_wait()
    {
        var cancels = new int[1];

        var provider = new FakeModelProvider(Provider)
            .WithModel(new ModelDescriptor { Name = Model })
            .WithModel(new ModelDescriptor { Name = EditorModel })
            .ForModel(Model, static cfg => cfg
                .CallsTool(StartTask, new { agentName = "researcher", input = "cancel it", description = "cancel it" })
                .CallsTool(WaitForCompletion, new { taskIds = new[] { 1 } })
                .CallsTool(GetResults, new { taskId = 1 })
                .EchoesLastToolResult("Delegated: "))
            .ForModel(EditorModel, static cfg => cfg.CallsTool(Tool, new { orderId = "ORD-7" }));

        await using var host = await TraconTestHost.StartAsync(builder =>
        {
            builder.AddModelProvider(provider);
            builder.AddAgent(new AgentDefinition
            {
                Name = Agent,
                Description = "Delegates the work.",
                Instructions = "Delegate.",
                Model = new ModelBinding { Provider = Provider, Model = Model },
                CallableAgents = ["researcher"],
                Origin = AgentDefinitionOrigin.Code,
            });
            builder.AddAgent(new AgentDefinition
            {
                Name = "researcher",
                Description = "Cancels orders.",
                Instructions = "Research.",
                Model = new ModelBinding { Provider = Provider, Model = EditorModel },
                ToolNames = [Tool],
                Origin = AgentDefinitionOrigin.Code,
            });
            AddApprovalTool(builder, Tool, cancels);
            builder.UseWorkflows();
        });

        await SaveAsync(host, "delegating", WorkflowKind.Sequential, [Agent]);

        var run = await RunToApprovalAsync(host, "delegating");

        // That decision holds below the step: the sub-agent's approval is a tool result the step's model reads.
        run.Run.Status.ShouldBe(RunStatus.Completed, run.Run.Error?.Message);
        run.Requests.ShouldBeEmpty();
        cancels[0].ShouldBe(0);

        var rows = await host.Services.GetRequiredService<IRunStore>()
            .QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 });
        var sub = rows.Single(static row => string.Equals(row.AgentName, "researcher", StringComparison.Ordinal));

        sub.Status.ShouldBe(RunStatus.Failed);
        sub.Error.ShouldNotBeNull().Message.ShouldContain("A child agent cannot request approval", Case.Sensitive);
    }

    /// <summary>
    /// A skill tool is a tool that needs approval like any other: the step waits for the
    /// answer when the definition leaves the read approval on, and does not when the definition turns it off.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_step_that_loads_a_skill_waits_for_an_approval_only_when_the_read_approval_is_on(bool disableReadApproval)
    {
        const string skill = "house-style";
        const string skillText = "Answer every question in exactly three words.";

        var provider = new FakeModelProvider(Provider)
            .WithModel(new ModelDescriptor { Name = Model })
            .CallsTool("load_skill", new { skillName = skill })
            .EchoesLastToolResult();

        await using var host = await TraconTestHost.StartAsync(builder =>
        {
            builder.AddModelProvider(provider);
            builder.AddSkill(new AgentSkillDefinition
            {
                TenantId = "default",
                Name = skill,
                Description = "How every reply is worded.",
                Instructions = skillText,
            });
            builder.AddAgent(new AgentDefinition
            {
                Name = Agent,
                Instructions = "Use the skill.",
                Model = new ModelBinding { Provider = Provider, Model = Model },
                SkillNames = [skill],
                Skills = disableReadApproval ? new SkillSettings { DisableReadApproval = true } : null,
                Origin = AgentDefinitionOrigin.Code,
            });
            builder.UseWorkflows();
        });

        await SaveAsync(host, "skilled-chain", WorkflowKind.Sequential, [Agent]);

        var run = await RunToApprovalAsync(host, "skilled-chain");

        if (disableReadApproval)
        {
            run.Run.Status.ShouldBe(RunStatus.Completed, run.Run.Error?.Message);
            run.Requests.ShouldBeEmpty("the definition turned the read approval off");

            return;
        }

        run.Run.Status.ShouldBe(RunStatus.AwaitingInput, run.Run.Error?.Message);

        var request = run.Requests.ShouldHaveSingleItem();

        request.Form.ShouldBe("ToolApproval");
        request.Prompt.ShouldContain("load_skill", Case.Sensitive);

        using var answer = await RespondAsync(host, run, new { approved = true });

        answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());

        var resumed = (await ReadWaitingAsync(host, await ReadRunIdAsync(answer))).Run;

        resumed.Status.ShouldBe(RunStatus.Completed, resumed.Error?.Message);
    }

    [Fact]
    public async Task A_request_nobody_answers_runs_out_of_time_and_the_step_that_waited_fails_with_the_workflow()
    {
        // of the phase, option A: a step left AwaitingApproval for good would show an approval that
        // nothing can answer any more.
        var limit = TimeSpan.FromMinutes(5);
        var clock = new ManualTimeProvider();
        var calls = new int[1];

        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(PricedProvider().CallsTool(Tool, new { orderId = "ORD-7" }).EchoesLastToolResult());
                builder.AddAgent(WriterAgent(format: null, Tool));
                AddApprovalTool(builder, Tool, calls);
                builder.UseWorkflows(static options => options.RequestExpirationScanInterval = TimeSpan.FromMilliseconds(50));
                builder.AddWorkflow(
                    "timed-approval",
                    static services => AgentWorkflowBuilder.BuildSequential(
                        "timed-approval", [services.GetWorkflowAgent("timed-approval", Agent)]),
                    options => options.RequestTimeout = limit);
            },
            configureServices: services => services.AddSingleton<TimeProvider>(clock));

        var waiting = await RunToApprovalAsync(host, "timed-approval");

        (await StepRowsAsync(host, waiting.Run.Id)).ShouldHaveSingleItem().Status.ShouldBe(RunStatus.AwaitingApproval);

        clock.Advance(limit + TimeSpan.FromMinutes(1));
        await host.WaitForRunStatusAsync(waiting.Run.Id, "Failed");

        var step = (await StepRowsAsync(host, waiting.Run.Id)).ShouldHaveSingleItem();

        step.Status.ShouldBe(RunStatus.Failed);
        step.Error.ShouldNotBeNull().Type.ShouldBe("WorkflowRequestExpired");
        calls[0].ShouldBe(0);

        // The answer that comes too late is refused, as an answer to any run that no longer waits is.
        using var late = await RespondAsync(host, waiting, new { approved = true });

        late.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        calls[0].ShouldBe(0);
    }

    [Fact]
    public async Task A_run_with_one_answered_and_one_unanswered_approval_closes_only_the_step_nobody_answered()
    {
        // The answered step stays as it closed: its answer continued on a new run and the tool ran. The step whose
        // request ran out closes with the workflow. Closing both would show a step as failed that did its work.
        var limit = TimeSpan.FromMinutes(5);
        var clock = new ManualTimeProvider();
        var cancels = new int[1];
        var refunds = new int[1];

        var provider = new FakeModelProvider(Provider)
            .WithModel(new ModelDescriptor { Name = Model })
            .WithModel(new ModelDescriptor { Name = EditorModel })
            .ForModel(Model, static cfg => cfg.CallsTool(Tool, new { orderId = "ORD-7" }).EchoesLastToolResult())
            .ForModel(EditorModel, static cfg => cfg.CallsTool(RefundTool, new { orderId = "ORD-7" }).EchoesLastToolResult());

        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(provider);
                builder.AddAgent(WriterAgent(format: null, Tool));
                builder.AddAgent(new AgentDefinition
                {
                    Name = "editor",
                    Instructions = "Edit.",
                    Model = new ModelBinding { Provider = Provider, Model = EditorModel },
                    ToolNames = [RefundTool],
                    Origin = AgentDefinitionOrigin.Code,
                });
                AddApprovalTool(builder, Tool, cancels);
                AddApprovalTool(builder, RefundTool, refunds);
                builder.UseWorkflows(static options => options.RequestExpirationScanInterval = TimeSpan.FromMilliseconds(50));
                builder.AddWorkflow(
                    "timed-both",
                    static services => AgentWorkflowBuilder.BuildConcurrent(
                        "timed-both",
                        [services.GetWorkflowAgent("timed-both", Agent), services.GetWorkflowAgent("timed-both", "editor")]),
                    options => options.RequestTimeout = limit);
            },
            configureServices: services => services.AddSingleton<TimeProvider>(clock));

        var waiting = await RunToApprovalAsync(host, "timed-both");

        waiting.Requests.Count.ShouldBe(2);

        var cancelRequest = waiting.Requests.Single(static request => request.Prompt.Contains(Tool, StringComparison.Ordinal));
        using var answered = await RespondAsync(host, waiting.Run.Id, cancelRequest.Id, new { approved = true });

        answered.StatusCode.ShouldBe(HttpStatusCode.OK, await answered.Content.ReadAsStringAsync());
        cancels[0].ShouldBe(1);

        clock.Advance(limit + TimeSpan.FromMinutes(1));
        await host.WaitForRunStatusAsync(waiting.Run.Id, "Failed");

        var steps = await StepRowsAsync(host, waiting.Run.Id);

        steps.Single(static step => string.Equals(step.AgentName, Agent, StringComparison.Ordinal)).Status.ShouldBe(RunStatus.AwaitingApproval);

        var unanswered = steps.Single(static step => string.Equals(step.AgentName, "editor", StringComparison.Ordinal));

        unanswered.Status.ShouldBe(RunStatus.Failed);
        unanswered.Error.ShouldNotBeNull().Type.ShouldBe("WorkflowRequestExpired");
        refunds[0].ShouldBe(0);
    }

    [Fact]
    public async Task Two_agents_of_a_group_chat_can_ask_and_the_chat_goes_on_after_the_answer()
    {
        // The pattern hosts its participants with the framework's host executor like the others; measured here
        // because the behaviour was read from decompiled code (the group chat runs one participant at a time).
        var cancels = new int[1];

        var provider = new FakeModelProvider(Provider)
            .WithModel(new ModelDescriptor { Name = Model })
            .WithModel(new ModelDescriptor { Name = EditorModel })
            .ForModel(Model, static cfg => cfg.CallsTool(Tool, new { orderId = "ORD-7" }).EchoesLastToolResult())
            .ForModel(EditorModel, static cfg => cfg.RespondsWith("edited"));

        await using var host = await TraconTestHost.StartAsync(builder =>
        {
            builder.AddModelProvider(provider);
            builder.AddAgent(WriterAgent(format: null, Tool));
            builder.AddAgent(new AgentDefinition
            {
                Name = "editor",
                Instructions = "Edit.",
                Model = new ModelBinding { Provider = Provider, Model = EditorModel },
                Origin = AgentDefinitionOrigin.Code,
            });
            AddApprovalTool(builder, Tool, cancels);
            builder.UseWorkflows();
        });

        using (var saved = await host.Client.PutAsJsonAsync(
                   "/tracon/api/workflows/chat",
                   new WorkflowSaveRequest { Kind = WorkflowKind.GroupChat, AgentNames = [Agent, "editor"], MaxIterations = 2 }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        var waiting = await RunToApprovalAsync(host, "chat");

        waiting.Run.Status.ShouldBe(RunStatus.AwaitingInput, waiting.Run.Error?.Message);
        waiting.Form.ShouldBe("ToolApproval");
        cancels[0].ShouldBe(0);

        using var answer = await RespondAsync(host, waiting, new { approved = true });

        answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());

        var resumed = (await ReadWaitingAsync(host, await ReadRunIdAsync(answer))).Run;

        resumed.Status.ShouldBe(RunStatus.Completed, resumed.Error?.Message);
        cancels[0].ShouldBe(1);
    }

    [Fact]
    public async Task A_magentic_participant_waits_for_an_approval_and_the_task_goes_on_after_the_answer()
    {
        // An earlier change left Magentic refusing because it was not measured. The orchestrator hosts its participants with
        // the framework's host executor like the other patterns (MAF-KAPSAMA #49); this case is that measurement.
        var cancels = new int[1];

        await using var host = await StartMagenticAsync(cancels, ManagerScript());

        await SaveMagenticAsync(host, requirePlanApproval: false);

        var waiting = await RunToApprovalAsync(host, "orchestrated");

        waiting.Run.Status.ShouldBe(RunStatus.AwaitingInput, waiting.Run.Error?.Message);
        waiting.Form.ShouldBe("ToolApproval");
        waiting.Prompt.ShouldContain(Tool, Case.Sensitive);
        cancels[0].ShouldBe(0);

        using var answer = await RespondAsync(host, waiting, new { approved = true });

        answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());

        var resumed = (await ReadWaitingAsync(host, await ReadRunIdAsync(answer))).Run;

        resumed.Status.ShouldBe(RunStatus.Completed, resumed.Error?.Message);
        cancels[0].ShouldBe(1);
    }

    [Fact]
    public async Task Refusing_a_magentic_participants_tool_runs_no_tool_and_the_task_goes_on()
    {
        var cancels = new int[1];

        await using var host = await StartMagenticAsync(cancels, ManagerScript());

        await SaveMagenticAsync(host, requirePlanApproval: false);

        var waiting = await RunToApprovalAsync(host, "orchestrated");

        using var answer = await RespondAsync(host, waiting, new { approved = false, text = "not today" });

        answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());

        var resumed = (await ReadWaitingAsync(host, await ReadRunIdAsync(answer))).Run;

        resumed.Status.ShouldBe(RunStatus.Completed, resumed.Error?.Message);
        cancels[0].ShouldBe(0, "the refused tool must not run");
    }

    [Fact]
    public async Task A_magentic_plan_review_and_a_participants_tool_approval_are_answered_one_after_the_other()
    {
        var cancels = new int[1];

        await using var host = await StartMagenticAsync(cancels, ManagerScript());

        await SaveMagenticAsync(host, requirePlanApproval: true);

        var plan = await RunToApprovalAsync(host, "orchestrated");

        plan.Run.Status.ShouldBe(RunStatus.AwaitingInput, plan.Run.Error?.Message);
        plan.Form.ShouldBe("PlanReview");

        using var planAnswer = await RespondAsync(host, plan, new { approved = true });

        planAnswer.StatusCode.ShouldBe(HttpStatusCode.OK, await planAnswer.Content.ReadAsStringAsync());

        var tool = await ReadWaitingAsync(host, await ReadRunIdAsync(planAnswer));

        tool.Run.Status.ShouldBe(RunStatus.AwaitingInput, tool.Run.Error?.Message);
        tool.Form.ShouldBe("ToolApproval");
        cancels[0].ShouldBe(0);

        using var toolAnswer = await RespondAsync(host, tool, new { approved = true });

        toolAnswer.StatusCode.ShouldBe(HttpStatusCode.OK, await toolAnswer.Content.ReadAsStringAsync());

        var finished = (await ReadWaitingAsync(host, await ReadRunIdAsync(toolAnswer))).Run;

        finished.Status.ShouldBe(RunStatus.Completed, finished.Error?.Message);
        cancels[0].ShouldBe(1);
    }

    [Fact]
    public async Task A_tool_of_the_magentic_manager_that_needs_approval_still_fails_the_workflow()
    {
        // The manager plans the task; the framework calls it directly, not through a host executor, so nothing
        // would raise its request.
        var cancels = new int[1];

        var provider = new FakeModelProvider(Provider)
            .WithModel(new ModelDescriptor { Name = Model })
            .WithModel(new ModelDescriptor { Name = EditorModel })
            .ForModel(Model, static cfg => cfg.RespondsWith("written"))
            .ForModel(EditorModel, static cfg => cfg.CallsTool(Tool, new { orderId = "ORD-7" }).EchoesLastToolResult());

        await using var host = await TraconTestHost.StartAsync(builder =>
        {
            builder.AddModelProvider(provider);
            builder.AddAgent(WriterAgent(format: null));
            builder.AddAgent(new AgentDefinition
            {
                Name = "manager",
                Instructions = "Manage.",
                Model = new ModelBinding { Provider = Provider, Model = EditorModel },
                ToolNames = [Tool],
                Origin = AgentDefinitionOrigin.Code,
            });
            AddApprovalTool(builder, Tool, cancels);
            builder.UseWorkflows();
        });

        await SaveMagenticAsync(host, requirePlanApproval: false);

        var run = await RunToApprovalAsync(host, "orchestrated");

        run.Run.Status.ShouldBe(RunStatus.Failed);
        run.Run.Error.ShouldNotBeNull().Message.ShouldContain("cannot wait for a tool approval", Case.Sensitive);
        run.Requests.ShouldBeEmpty();
        cancels[0].ShouldBe(0);
    }

    /// <summary>The manager's five answers: facts, plan, a ledger that hands the task to the writer, a ledger that ends it, the final answer.</summary>
    private static string[] ManagerScript() =>
    [
        "FACTS: an order must be canceled.",
        "PLAN: the writer cancels ORD-7.",
        Ledger(satisfied: false, instruction: "Cancel order ORD-7."),
        Ledger(satisfied: true, instruction: "Nothing more."),
        "FINAL: ORD-7 was handled.",
    ];

    private static string Ledger(bool satisfied, string instruction)
        => $$$"""
           {"is_request_satisfied":{"reason":"r","answer":{{{(satisfied ? "true" : "false")}}}},
            "is_in_loop":{"reason":"r","answer":false},
            "is_progress_being_made":{"reason":"r","answer":true},
            "next_speaker":{"reason":"r","answer":"{{{Agent}}}"},
            "instruction_or_question":{"reason":"r","answer":"{{{instruction}}}"}}
           """;

    private static Task<TraconTestHost> StartMagenticAsync(int[] cancels, string[] managerScript)
    {
        var provider = new FakeModelProvider(Provider)
            .WithModel(new ModelDescriptor { Name = Model })
            .WithModel(new ModelDescriptor { Name = EditorModel })
            .ForModel(Model, static cfg => cfg.CallsTool(Tool, new { orderId = "ORD-7" }).EchoesLastToolResult())
            .ForModel(EditorModel, cfg => cfg.RespondsWith(managerScript));

        return TraconTestHost.StartAsync(builder =>
        {
            builder.AddModelProvider(provider);
            builder.AddAgent(WriterAgent(format: null, Tool));
            builder.AddAgent(new AgentDefinition
            {
                Name = "manager",
                Instructions = "Manage.",
                Model = new ModelBinding { Provider = Provider, Model = EditorModel },
                Origin = AgentDefinitionOrigin.Code,
            });
            AddApprovalTool(builder, Tool, cancels);
            builder.UseWorkflows();
        });
    }

    private static async Task SaveMagenticAsync(TraconTestHost host, bool requirePlanApproval)
    {
        using var saved = await host.Client.PutAsJsonAsync(
            "/tracon/api/workflows/orchestrated",
            new WorkflowSaveRequest
            {
                Kind = WorkflowKind.Magentic,
                ManagerAgentName = "manager",
                AgentNames = [Agent],
                MaxIterations = 5,
                RequirePlanApproval = requirePlanApproval,
            });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    private static Task<TraconTestHost> StartTwoAgentsAsync(int[] cancels, int[] refunds)
    {
        var provider = new FakeModelProvider(Provider)
            .WithModel(new ModelDescriptor { Name = Model })
            .WithModel(new ModelDescriptor { Name = EditorModel })
            .ForModel(Model, static cfg => cfg.CallsTool(Tool, new { orderId = "ORD-7" }).EchoesLastToolResult())
            .ForModel(EditorModel, static cfg => cfg.CallsTool(RefundTool, new { orderId = "ORD-7" }).EchoesLastToolResult());

        return TraconTestHost.StartAsync(builder =>
        {
            builder.AddModelProvider(provider);
            builder.AddAgent(WriterAgent(format: null, Tool));
            builder.AddAgent(new AgentDefinition
            {
                Name = "editor",
                Instructions = "Edit.",
                Model = new ModelBinding { Provider = Provider, Model = EditorModel },
                ToolNames = [RefundTool],
                Origin = AgentDefinitionOrigin.Code,
            });
            AddApprovalTool(builder, Tool, cancels);
            AddApprovalTool(builder, RefundTool, refunds);
            builder.UseWorkflows();
        });
    }
}
