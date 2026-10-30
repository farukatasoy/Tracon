using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.AgentStepHost;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.ToolApprovalWorkflowHost;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A workflow step whose agent calls a tool that needs approval stops the workflow, and a person's answer
/// from <c>POST /api/workflows/runs/{runId}/respond</c> continues it.
/// </summary>
/// <remarks>
/// <para>
/// Both ways into a host that turns the approval into a request run: a stored chain of agents (a ready-made
/// pattern) and a graph built in code over <c>GetWorkflowAgent</c>. A step that is a plain function executor -
/// a mixed chain's agent node, <c>GetWorkflowAgentStep</c> - cannot wait, and still fails the workflow
/// (<see cref="WorkflowAgentStepRecordingTests"/> holds that). The patterns beyond Sequential are in
/// <see cref="WorkflowToolApprovalPatternTests"/>.
/// </para>
/// <para>
/// Before this change the step refused the approval and failed the workflow, so a workflow could not use a
/// tool that needs a person's approval unless an auto-approval rule existed.
/// </para>
/// </remarks>
public sealed class WorkflowToolApprovalTests
{
    [Theory]
    [InlineData(StoredApproval)]
    [InlineData(CodeApproval)]
    public async Task A_step_whose_tool_needs_approval_waits_for_an_answer_instead_of_failing_the_workflow(string workflow)
    {
        var calls = new int[1];
        await using var host = await StartApprovalHostAsync(calls);

        var waiting = await RunToApprovalAsync(host, workflow);

        // The workflow waits at its checkpoint; the tool has not run.
        waiting.Run.Status.ShouldBe(RunStatus.AwaitingInput, waiting.Run.Error?.Message);
        waiting.Form.ShouldBe("ToolApproval");
        waiting.Prompt.ShouldContain(Tool, Case.Sensitive);
        calls[0].ShouldBe(0, "the tool ran before anyone approved it");

        // The step's row says what it waits for. It is not Failed: the workflow can go on.
        var step = (await StepRowsAsync(host, waiting.Run.Id)).ShouldHaveSingleItem();

        step.Status.ShouldBe(RunStatus.AwaitingApproval);

        // The decision is made from the workflow's request alone: no second approval queue entry.
        (await host.Services.GetRequiredService<IPendingApprovalStore>().ListPendingAsync()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(StoredApproval)]
    [InlineData(CodeApproval)]
    public async Task Approving_runs_the_tool_once_and_the_workflow_completes(string workflow)
    {
        var calls = new int[1];
        await using var host = await StartApprovalHostAsync(calls);
        var waiting = await RunToApprovalAsync(host, workflow);

        using var answer = await RespondAsync(host, waiting, new { approved = true, text = "checked with the customer" });

        answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());

        var resumed = (await ReadWaitingAsync(host, await ReadRunIdAsync(answer))).Run;

        resumed.Status.ShouldBe(RunStatus.Completed, resumed.Error?.Message);
        calls[0].ShouldBe(1, "the approved tool must run exactly once");

        // The decision is in the audit trail, by tool.
        var audit = await host.Services.GetRequiredService<IAuditLog>()
            .QueryAsync(new AuditQuery { Action = "approval.decision" });
        var entry = audit.ShouldHaveSingleItem();

        entry.Entity.ShouldBe($"tool:{Tool}");
        entry.After.ShouldNotBeNull().ShouldContain("\"approved\":true", Case.Sensitive);
    }

    [Theory]
    [InlineData(StoredApproval)]
    [InlineData(CodeApproval)]
    public async Task Refusing_runs_no_tool_and_the_workflow_goes_on(string workflow)
    {
        var calls = new int[1];
        await using var host = await StartApprovalHostAsync(calls);
        var waiting = await RunToApprovalAsync(host, workflow);

        using var answer = await RespondAsync(host, waiting, new { approved = false, text = "not this order" });

        answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());

        var resumed = (await ReadWaitingAsync(host, await ReadRunIdAsync(answer))).Run;

        resumed.Status.ShouldBe(RunStatus.Completed, resumed.Error?.Message);
        calls[0].ShouldBe(0, "a refused tool must not run");
    }

    [Fact]
    public async Task The_prompt_carries_what_the_presenter_says_about_the_call_and_never_the_arguments()
    {
        await using var host = await StartApprovalHostAsync(
            new int[1],
            services => services.AddSingleton<IToolApprovalPresenter>(new FixedPresenter(new ToolApprovalPresentation
            {
                EntityType = "order",
                Message = "Cancels the order of Ada Lovelace.",
            })));

        var waiting = await RunToApprovalAsync(host, StoredApproval);

        // The presenter chooses what to show; the arguments (ORD-7) are not copied into the request.
        waiting.Prompt.ShouldBe($"{Tool}: Cancels the order of Ada Lovelace.");
        waiting.Prompt.ShouldNotContain("ORD-7", Case.Sensitive);
    }

    [Fact]
    public async Task A_presenter_that_fails_costs_the_request_nothing_but_the_sentence()
    {
        await using var host = await StartApprovalHostAsync(
            new int[1],
            services => services.AddSingleton<IToolApprovalPresenter>(new FixedPresenter(presentation: null)));

        var waiting = await RunToApprovalAsync(host, StoredApproval);

        waiting.Run.Status.ShouldBe(RunStatus.AwaitingInput, waiting.Run.Error?.Message);
        waiting.Prompt.ShouldBe(Tool);
    }

    [Fact]
    public async Task A_waiting_workflow_is_answered_by_a_host_started_after_the_first_one_stopped()
    {
        // 230.6: the request and the call live in the checkpoint, so an answer given after a restart still runs
        // the tool once. Two hosts, one SQLite file.
        using var database = new TempSqliteDatabase("tool-approval-restart");
        var calls = new int[1];
        WaitingRun waiting;

        await using (var first = await StartApprovalHostAsync(
                         PricedProvider().CallsTool(Tool, new { orderId = "ORD-7" }).EchoesLastToolResult(),
                         calls,
                         configureTracon: builder => builder.UseSqlite(database.ConnectionString)))
        {
            waiting = await RunToApprovalAsync(first, StoredApproval);
            waiting.Run.Status.ShouldBe(RunStatus.AwaitingInput, waiting.Run.Error?.Message);
        }

        await using var second = await StartApprovalHostAsync(
            PricedProvider().EchoesLastToolResult(),
            calls,
            configureTracon: builder => builder.UseSqlite(database.ConnectionString),
            saveStoredChain: false);

        calls[0].ShouldBe(0);

        using var answer = await RespondAsync(second, waiting, new { approved = true });

        answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());

        var resumed = (await ReadWaitingAsync(second, await ReadRunIdAsync(answer))).Run;

        resumed.Status.ShouldBe(RunStatus.Completed, resumed.Error?.Message);
        calls[0].ShouldBe(1, "the approved tool must run exactly once, in the host that took the answer");
    }

    [Theory]
    [InlineData(StoredApproval)]
    [InlineData(CodeApproval)]
    public async Task An_approval_is_answered_once_even_in_the_default_mode_where_every_other_answer_opens_a_branch(string workflow)
    {
        // The default mode opens a new run for every answer. For a tool approval that would run the tool again for
        // each "approve": a second click, a client that retries a dropped stream. The approval inbox refuses a second
        // decision, and so does this.
        var calls = new int[1];
        await using var host = await StartApprovalHostAsync(calls);
        var waiting = await RunToApprovalAsync(host, workflow);

        using var first = await RespondAsync(host, waiting, new { approved = true });
        var firstRun = await ReadRunIdAsync(first);

        using var repeat = await RespondAsync(host, waiting, new { approved = true });

        repeat.StatusCode.ShouldBe(HttpStatusCode.OK, await repeat.Content.ReadAsStringAsync());
        (await ReadRunIdAsync(repeat)).ShouldBe(firstRun, "the same decision follows the run the first one started");

        using var other = await RespondAsync(host, waiting, new { approved = false });

        other.StatusCode.ShouldBe(HttpStatusCode.Conflict, await other.Content.ReadAsStringAsync());
        calls[0].ShouldBe(1, "the approved tool must run exactly once");
        (await host.Services.GetRequiredService<IAuditLog>().QueryAsync(new AuditQuery { Action = "approval.decision" }))
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_record_cut_inside_the_prompt_still_names_the_tool_so_the_decision_is_audited_and_remembered()
    {
        // The facts come before the prompt, so a payload cut at MaxPayloadLength loses the prompt's tail first.
        var length = await FactsEndAsync();
        var calls = new int[1];

        await using var host = await StartApprovalHostAsync(
            calls,
            services => services.Configure<TraconOptions>(options => options.RunRecording.MaxPayloadLength = length + 10));

        var waiting = await RunToApprovalAsync(host, StoredApproval);

        waiting.Run.Status.ShouldBe(RunStatus.AwaitingInput, waiting.Run.Error?.Message);

        using var answer = await RespondAsync(host, waiting, new { approved = true, remember = true });

        answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());
        (await ReadWaitingAsync(host, await ReadRunIdAsync(answer))).Run.Status.ShouldBe(RunStatus.Completed);
        calls[0].ShouldBe(1);
        (await host.Services.GetRequiredService<IAuditLog>().QueryAsync(new AuditQuery { Action = "approval.decision" }))
            .ShouldHaveSingleItem().Entity.ShouldBe($"tool:{Tool}");
        (await host.Services.GetRequiredService<IToolApprovalRuleStore>().ListAsync(TenantId(host)))
            .ShouldHaveSingleItem().AgentName.ShouldBe(Agent);
    }

    [Fact]
    public async Task A_record_cut_before_it_names_the_tool_refuses_the_answer_instead_of_applying_it_unrecorded()
    {
        // A decision that cannot be audited is not applied. A cut that leaves nothing to audit used to read
        // as "not a tool approval": the answer went through with no audit row and no rule, and the tool ran.
        var calls = new int[1];

        await using var host = await StartApprovalHostAsync(
            calls,
            services => services.Configure<TraconOptions>(options => options.RunRecording.MaxPayloadLength = 300));

        var waiting = await RunToApprovalAsync(host, StoredApproval);

        waiting.Run.Status.ShouldBe(RunStatus.AwaitingInput, waiting.Run.Error?.Message);

        // The request is still listed and still has its form: the early check reads the part that survived.
        waiting.Form.ShouldBe("ToolApproval");

        var before = await CountRunsAsync(host);

        using var answer = await RespondAsync(host, waiting, new { approved = true });

        answer.StatusCode.ShouldBe(HttpStatusCode.InternalServerError, await answer.Content.ReadAsStringAsync());
        (await TraconTestHost.ReadJsonAsync(answer)).GetProperty("detail").GetString()!.ShouldContain("MaxPayloadLength", Case.Sensitive);
        calls[0].ShouldBe(0);
        (await CountRunsAsync(host)).ShouldBe(before);
        (await host.Services.GetRequiredService<IAuditLog>().QueryAsync(new AuditQuery { Action = "approval.decision" })).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_data_value_is_refused_and_opens_no_run_because_the_tool_call_comes_from_the_request()
    {
        var calls = new int[1];
        await using var host = await StartApprovalHostAsync(calls);
        var waiting = await RunToApprovalAsync(host, StoredApproval);
        var before = await CountRunsAsync(host);

        // A caller that could send the tool call could approve a different one than the workflow asked about.
        using var answer = await RespondAsync(
            host, waiting, new { approved = true, data = new { name = Tool, arguments = new { orderId = "ORD-999" } } });

        answer.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await answer.Content.ReadAsStringAsync());
        (await TraconTestHost.ReadJsonAsync(answer)).GetProperty("detail").GetString()!.ShouldContain("'data'", Case.Sensitive);
        (await CountRunsAsync(host)).ShouldBe(before);
        calls[0].ShouldBe(0);
    }

    [Fact]
    public async Task An_answer_without_a_yes_or_no_is_refused()
    {
        await using var host = await StartApprovalHostAsync(new int[1]);
        var waiting = await RunToApprovalAsync(host, StoredApproval);
        var before = await CountRunsAsync(host);

        using var answer = await RespondAsync(host, waiting, new { text = "maybe" });

        answer.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await answer.Content.ReadAsStringAsync());
        (await TraconTestHost.ReadJsonAsync(answer)).GetProperty("detail").GetString()!.ShouldContain("'approved'", Case.Sensitive);
        (await CountRunsAsync(host)).ShouldBe(before);
    }

    [Fact]
    public async Task Remembering_a_refusal_is_refused()
    {
        await using var host = await StartApprovalHostAsync(new int[1]);
        var waiting = await RunToApprovalAsync(host, StoredApproval);
        var before = await CountRunsAsync(host);

        using var answer = await RespondAsync(host, waiting, new { approved = false, remember = true });

        answer.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await answer.Content.ReadAsStringAsync());
        (await TraconTestHost.ReadJsonAsync(answer)).GetProperty("detail").GetString()!.ShouldContain("'remember'", Case.Sensitive);
        (await CountRunsAsync(host)).ShouldBe(before);
        (await host.Services.GetRequiredService<IToolApprovalRuleStore>().ListAsync(TenantId(host))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Remember_is_refused_on_a_request_that_is_not_a_tool_approval()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder
                .UseWorkflows()
                .AddWorkflow("note-flow", static _ => BuildNoteWorkflow(), "Waits for a note."));

        using var started = await host.Client.PostAsJsonAsync(
            "/tracon/api/workflows/note-flow/run",
            new WorkflowRunHttpRequest { Message = "the draft" });
        var runId = await ReadRunIdAsync(started);
        using var listed = await host.Client.GetAsync(new Uri($"/tracon/api/workflows/runs/{runId}/requests", UriKind.Relative));
        var requestId = (await TraconTestHost.ReadJsonAsync(listed))[0].GetProperty("requestId").GetString()!;

        using var answer = await host.Client.PostAsJsonAsync(
            $"/tracon/api/workflows/runs/{runId}/respond",
            new { requestId, text = "noted", remember = true });

        answer.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await answer.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(StoredApproval)]
    [InlineData(CodeApproval)]
    public async Task Remembering_an_approval_saves_a_rule_for_the_agent_and_the_tool_and_the_next_run_asks_nothing(string workflow)
    {
        var calls = new int[1];

        // Two runs call the tool. The model's turns in order: the first run's call, the answer that follows the
        // approval, the second run's call; the echo after the tool's result is the fallback.
        await using var host = await StartApprovalHostAsync(
            PricedProvider()
                .CallsTool(Tool, new { orderId = "ORD-7" })
                .RespondsWith("The order is canceled.")
                .CallsTool(Tool, new { orderId = "ORD-7" })
                .EchoesLastToolResult(),
            calls);
        var waiting = await RunToApprovalAsync(host, workflow);

        using var answer = await RespondAsync(host, waiting, new { approved = true, remember = true });

        answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());
        (await ReadWaitingAsync(host, await ReadRunIdAsync(answer))).Run.Status.ShouldBe(RunStatus.Completed);
        calls[0].ShouldBe(1);

        var rule = (await host.Services.GetRequiredService<IToolApprovalRuleStore>().ListAsync(TenantId(host))).ShouldHaveSingleItem();

        rule.AgentName.ShouldBe(Agent);
        rule.ToolName.ShouldBe(Tool);

        // The rule is the agent's own: a second run of the workflow does not wait.
        var second = await RunToApprovalAsync(host, workflow);

        second.Run.Status.ShouldBe(RunStatus.Completed, second.Run.Error?.Message);
        calls[0].ShouldBe(2);
    }

    [Fact]
    public async Task A_rule_is_saved_under_the_agents_real_name_though_the_framework_rewrites_a_hyphen_in_its_executor_id()
    {
        // MAF names an executor "{agent name}_{id}" with the characters it dislikes replaced: "order-writer"
        // is "order_writer" there. A rule saved under that name would never match the agent, and "do not ask
        // again" would quietly stop working.
        const string hyphenated = "order-writer";
        var calls = new int[1];

        await using var host = await StartAsync(
            PricedProvider().CallsTool(Tool, new { orderId = "ORD-7" }).EchoesLastToolResult(),
            new AgentDefinition
            {
                Name = hyphenated,
                Instructions = "Write.",
                Model = new ModelBinding { Provider = Provider, Model = Model },
                ToolNames = [Tool],
                Origin = AgentDefinitionOrigin.Code,
            },
            builder =>
            {
                AddApprovalTool(builder, Tool, calls);
                builder.AddWorkflow(
                    "hyphenated-flow",
                    static services => AgentWorkflowBuilder.BuildSequential(
                        "hyphenated-flow", [services.GetWorkflowAgent("hyphenated-flow", hyphenated)]));
            });

        var waiting = await RunToApprovalAsync(host, "hyphenated-flow");

        using var answer = await RespondAsync(host, waiting, new { approved = true, remember = true });

        answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());
        (await ReadWaitingAsync(host, await ReadRunIdAsync(answer))).Run.Status.ShouldBe(RunStatus.Completed);

        var rule = (await host.Services.GetRequiredService<IToolApprovalRuleStore>().ListAsync(TenantId(host))).ShouldHaveSingleItem();

        rule.AgentName.ShouldBe(hyphenated);
    }

    [Fact]
    public async Task An_approval_that_cannot_name_its_agent_cannot_be_remembered_but_can_be_answered()
    {
        // A graph that hosts an agent Tracon did not wrap names it only in the framework's rewritten form. A rule
        // for a guessed name would be one that never matches, so remembering is refused and nothing is saved.
        // One instance: the framework derives the executor id from it, and a graph rebuilt with a new one cannot
        // resume the checkpoint (the reason Tracon's own wrappers carry a permanent identity).
        var agent = new ForeignAskingAgent();

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .UseWorkflows()
            .AddWorkflow("foreign-agent", _ => AgentWorkflowBuilder.BuildSequential("foreign-agent", [agent])));

        var waiting = await RunToApprovalAsync(host, "foreign-agent");

        waiting.Form.ShouldBe("ToolApproval");

        using var remembered = await RespondAsync(host, waiting, new { approved = true, remember = true });

        remembered.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await remembered.Content.ReadAsStringAsync());
        (await TraconTestHost.ReadJsonAsync(remembered)).GetProperty("detail").GetString()!.ShouldContain("agent", Case.Sensitive);
        (await host.Services.GetRequiredService<IToolApprovalRuleStore>().ListAsync(TenantId(host))).ShouldBeEmpty();

        using var plain = await RespondAsync(host, waiting, new { approved = true });

        plain.StatusCode.ShouldBe(HttpStatusCode.OK, await plain.Content.ReadAsStringAsync());
        (await ReadWaitingAsync(host, await ReadRunIdAsync(plain))).Run.Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task A_decision_that_cannot_be_audited_is_not_applied_and_the_workflow_keeps_waiting()
    {
        var calls = new int[1];
        var audit = new SwitchableAuditLog();

        await using var host = await StartApprovalHostAsync(
            calls,
            services => services.AddSingleton<IAuditLog>(audit));

        var waiting = await RunToApprovalAsync(host, StoredApproval);

        audit.Fail = true;

        // The host has no exception handler, so a refusal surfaces as the exception; a real host answers 500.
        await Should.ThrowAsync<TraconException>(async () => await RespondAsync(host, waiting, new { approved = true }));

        calls[0].ShouldBe(0, "the tool ran although its approval was never recorded");
        (await ReadWaitingAsync(host, waiting.Run.Id)).Run.Status.ShouldBe(RunStatus.AwaitingInput);

        // The refusal is not a poison pill: the same answer is taken once the audit store recovers.
        audit.Fail = false;

        using var again = await RespondAsync(host, waiting, new { approved = true });

        again.StatusCode.ShouldBe(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());
        (await ReadWaitingAsync(host, await ReadRunIdAsync(again))).Run.Status.ShouldBe(RunStatus.Completed);
        calls[0].ShouldBe(1);
    }

    [Fact]
    public async Task Another_tenant_cannot_answer_the_request_and_nothing_is_applied()
    {
        var calls = new int[1];

        await using var host = await StartApprovalHostAsync(
            PricedProvider().CallsTool(Tool, new { orderId = "ORD-7" }).EchoesLastToolResult(),
            calls,
            configureTracon: static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }),
            saveStoredChain: false);

        // The stored chain belongs to a tenant; the code graph is registered for all of them.
        var waiting = await RunToApprovalAsync(host, CodeApproval, tenant: "acme");

        waiting.Run.Status.ShouldBe(RunStatus.AwaitingInput, waiting.Run.Error?.Message);

        using var answer = await RespondAsync(host, waiting, new { approved = true, remember = true }, tenant: "other");

        // The same answer a run that does not exist gets: whether it exists is not leaked.
        answer.StatusCode.ShouldBe(HttpStatusCode.NotFound, await answer.Content.ReadAsStringAsync());
        calls[0].ShouldBe(0);
        (await host.Services.GetRequiredService<IAuditLog>().QueryAsync(new AuditQuery { TenantId = "other", Action = "approval.decision" }))
            .ShouldBeEmpty();
        (await host.Services.GetRequiredService<IToolApprovalRuleStore>().ListAsync("other")).ShouldBeEmpty();
        (await host.Services.GetRequiredService<IToolApprovalRuleStore>().ListAsync("acme")).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_single_answer_workflow_follows_a_repeated_decision_and_applies_it_once()
    {
        var calls = new int[1];

        await using var host = await StartAsync(
            PricedProvider().CallsTool(Tool, new { orderId = "ORD-7" }).EchoesLastToolResult(),
            WriterAgent(format: null, Tool),
            builder =>
            {
                AddApprovalTool(builder, Tool, calls);
                builder.AddWorkflow(
                    "single-approval",
                    static services => AgentWorkflowBuilder.BuildSequential(
                        "single-approval", [services.GetWorkflowAgent("single-approval", Agent)]),
                    static options => options.ResponseMode = WorkflowResponseMode.SingleAnswer);
            });

        var waiting = await RunToApprovalAsync(host, "single-approval");

        using var first = await RespondAsync(host, waiting, new { approved = true, remember = true });
        var firstRun = await ReadRunIdAsync(first);

        // The same decision again starts nothing: it follows the run the first one started.
        using var repeat = await RespondAsync(host, waiting, new { approved = true, remember = true });

        repeat.StatusCode.ShouldBe(HttpStatusCode.OK, await repeat.Content.ReadAsStringAsync());
        (await ReadRunIdAsync(repeat)).ShouldBe(firstRun);

        // A different decision is a conflict, and a decision that did not start a run is not audited or remembered.
        using var other = await RespondAsync(host, waiting, new { approved = false });

        other.StatusCode.ShouldBe(HttpStatusCode.Conflict, await other.Content.ReadAsStringAsync());
        calls[0].ShouldBe(1);
        (await host.Services.GetRequiredService<IAuditLog>().QueryAsync(new AuditQuery { Action = "approval.decision" })).ShouldHaveSingleItem();
        (await host.Services.GetRequiredService<IToolApprovalRuleStore>().ListAsync(TenantId(host))).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_queued_answer_continues_the_workflow_in_the_background()
    {
        var calls = new int[1];
        await using var host = await StartApprovalHostAsync(calls);
        var waiting = await RunToApprovalAsync(host, StoredApproval);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/workflows/runs/{waiting.Run.Id}/respond")
        {
            Content = JsonContent.Create(new { requestId = waiting.RequestId, approved = true, text = "queued" }),
        };

        request.Headers.Add("Prefer", "respond-async");

        using var accepted = await host.Client.SendAsync(request);

        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());

        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        (await host.WaitForTerminalRunAsync(runId)).Status.ShouldBe(RunStatus.Completed);
        calls[0].ShouldBe(1);
    }

    /// <summary>
    /// How long the part of a tool approval's request record is that comes before its prompt: the event payload
    /// up to the prompt, as the default host writes it.
    /// </summary>
    private static async Task<int> FactsEndAsync()
    {
        await using var host = await StartApprovalHostAsync(new int[1]);
        var waiting = await RunToApprovalAsync(host, StoredApproval);
        var payload = string.Empty;

        await foreach (var runEvent in host.Services.GetRequiredService<IRunStore>().ReadEventsAsync(waiting.Run.Id, 0))
        {
            if (runEvent.Type == RunEventType.WorkflowRequest)
            {
                payload = runEvent.Payload ?? string.Empty;
            }
        }

        var end = payload.IndexOf(",\"prompt\"", StringComparison.Ordinal);

        end.ShouldBeGreaterThan(0, "the payload carries no prompt member: " + payload);

        return end;
    }

    private static Task<TraconTestHost> StartApprovalHostAsync(
        int[] calls,
        Action<IServiceCollection>? configureServices = null)
        => StartApprovalHostAsync(
            PricedProvider().CallsTool(Tool, new { orderId = "ORD-7" }).EchoesLastToolResult(),
            calls,
            configureServices);

    private static Task<TraconTestHost> StartApprovalHostAsync(
        FakeModelProvider provider,
        int[] calls,
        Action<IServiceCollection>? configureServices = null,
        Action<ITraconBuilder>? configureTracon = null,
        bool saveStoredChain = true)
        => ToolApprovalWorkflowHost.StartAsync(provider, calls, configureServices, configureTracon, saveStoredChain);

    private static Workflow BuildNoteWorkflow()
    {
        var port = RequestPort.Create<string, string>("note-port").BindAsExecutor(allowWrappedRequests: false);

        var start = ExecutorBindingExtensions.BindAsExecutor<List<ChatMessage>, string>(
            static messages => $"Comment on: {messages.LastOrDefault()?.Text ?? string.Empty}",
            id: "start");

        var end = ExecutorBindingExtensions.BindAsExecutor<string, string>(
            static note => $"noted: {note}",
            id: "end");

        return new WorkflowBuilder(start)
            .AddEdge(start, port)
            .AddEdge(port, end)
            .WithOutputFrom(end)
            .WithName("note-flow")
            .Build();
    }

    /// <summary>An agent that is not one of Tracon's wrappers: it asks for an approval, then finishes once answered.</summary>
    private sealed class ForeignAskingAgent : AIAgent
    {
        public override string Name => "foreign-agent";

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The pattern's host streams.");

        protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);

            var call = new FunctionCallContent(
                "call-1",
                Tool,
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["orderId"] = "ORD-7" });

            if (messages.SelectMany(static message => message.Contents).OfType<ToolApprovalResponseContent>().Any())
            {
                yield return new AgentResponseUpdate(ChatRole.Assistant, [call]);
                yield return new AgentResponseUpdate(ChatRole.Tool, [new FunctionResultContent("call-1", "done")]);
                yield return new AgentResponseUpdate(ChatRole.Assistant, [new TextContent("done")]);

                yield break;
            }

            yield return new AgentResponseUpdate(ChatRole.Assistant, [new ToolApprovalRequestContent("request-1", call)]);
        }

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
            => new(new ForeignSession());

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            JsonElement serializedState,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => new(new ForeignSession());

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
            AgentSession session,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => new(JsonDocument.Parse("{}").RootElement.Clone());

        private sealed class ForeignSession : AgentSession;
    }

    /// <summary>A presenter that gives one answer, or throws when it has none.</summary>
    private sealed class FixedPresenter(ToolApprovalPresentation? presentation) : IToolApprovalPresenter
    {
        public ValueTask<ToolApprovalPresentation?> PresentAsync(ToolApprovalContext context, CancellationToken cancellationToken = default)
            => presentation is null
                ? throw new InvalidOperationException("the order service is down")
                : new(presentation);
    }

    /// <summary>An audit log that keeps every entry and refuses all writes while <see cref="Fail"/> is set.</summary>
    private sealed class SwitchableAuditLog : IAuditLog
    {
        private readonly List<AuditEntry> _written = [];
        private readonly Lock _gate = new();

        public bool Fail { get; set; }

        public ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            if (Fail)
            {
                throw new InvalidOperationException("audit store is unreachable");
            }

            lock (_gate)
            {
                _written.Add(entry);
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return new(_written
                    .Where(entry => query.Action is null || string.Equals(entry.Action, query.Action, StringComparison.Ordinal))
                    .ToList());
            }
        }

        public ValueTask<AuditChainVerification> VerifyChainAsync(AuditChainQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("not exercised by these tests");
    }
}
