using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.AgentStepHost;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A workflow step whose agent calls a tool that needs approval stops the workflow, and a person's answer
/// from <c>POST /api/workflows/runs/{runId}/respond</c> continues it (phase 230, K-1051).
/// </summary>
/// <remarks>
/// <para>
/// Both ways into a host that turns the approval into a request run: a stored chain of agents (a ready-made
/// pattern) and a graph built in code over <c>GetWorkflowAgent</c>. A step that is a plain function executor -
/// a mixed chain's agent node, <c>GetWorkflowAgentStep</c> - cannot wait, and still fails the workflow
/// (<see cref="WorkflowAgentStepRecordingTests"/> holds that).
/// </para>
/// <para>
/// Before this phase the step refused the approval and failed the workflow, so a workflow could not use a
/// tool that needs a person's approval unless an auto-approval rule existed.
/// </para>
/// </remarks>
public sealed class WorkflowToolApprovalTests
{
    private const string Tool = "cancel_order";
    private const string StoredApproval = "approval-chain";
    private const string CodeApproval = "approval-code";

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

        // The decision is made from the workflow's request alone (K-372): no second approval queue entry.
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

        var resumed = await host.WaitForTerminalRunAsync(await ReadRunIdAsync(answer));

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

        var resumed = await host.WaitForTerminalRunAsync(await ReadRunIdAsync(answer));

        resumed.Status.ShouldBe(RunStatus.Completed, resumed.Error?.Message);
        calls[0].ShouldBe(0, "a refused tool must not run");
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
        (await host.WaitForTerminalRunAsync(await ReadRunIdAsync(answer))).Status.ShouldBe(RunStatus.Completed);
        calls[0].ShouldBe(1);

        var rule = (await host.Services.GetRequiredService<IToolApprovalRuleStore>().ListAsync(TenantId(host))).ShouldHaveSingleItem();

        rule.AgentName.ShouldBe(Agent);
        rule.ToolName.ShouldBe(Tool);

        // The rule is the agent's own: a second run of the workflow does not wait.
        var second = await RunToCompletionAsync(host, workflow);

        second.Status.ShouldBe(RunStatus.Completed, second.Error?.Message);
        calls[0].ShouldBe(2);
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

        calls[0].ShouldBe(0, "the tool ran although its approval was never recorded (K-089)");
        (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(waiting.Run.Id))
            .ShouldNotBeNull().Status.ShouldBe(RunStatus.AwaitingInput);

        // The refusal is not a poison pill: the same answer is taken once the audit store recovers.
        audit.Fail = false;

        using var again = await RespondAsync(host, waiting, new { approved = true });

        again.StatusCode.ShouldBe(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());
        (await host.WaitForTerminalRunAsync(await ReadRunIdAsync(again))).Status.ShouldBe(RunStatus.Completed);
        calls[0].ShouldBe(1);
    }

    private static Task<TraconTestHost> StartApprovalHostAsync(
        int[] calls,
        Action<IServiceCollection>? configureServices = null)
        => StartApprovalHostAsync(
            PricedProvider().CallsTool(Tool, new { orderId = "ORD-7" }).EchoesLastToolResult(),
            calls,
            configureServices);

    private static async Task<TraconTestHost> StartApprovalHostAsync(
        FakeModelProvider provider,
        int[] calls,
        Action<IServiceCollection>? configureServices = null)
    {
        var host = await StartAsync(
            provider,
            WriterAgent(format: null, Tool),
            builder =>
            {
                builder.AddTool(
                    (Func<string, string>)(orderId =>
                    {
                        Interlocked.Increment(ref calls[0]);

                        return $"{orderId} canceled.";
                    }),
                    name: Tool,
                    description: "Cancels an order.",
                    configure: options => options.RequiresApproval = true);

                // The code graph hosts the agent the way a ready-made pattern does.
                builder.AddWorkflow(
                    CodeApproval,
                    static services => AgentWorkflowBuilder.BuildSequential(
                        CodeApproval, [services.GetWorkflowAgent(CodeApproval, Agent)]));
            },
            configureServices);

        // The agent-names form is the ready-made pattern. The node-list form is a hand-built graph whose agent
        // nodes are plain function executors (a mixed chain), and those cannot wait for an approval.
        using var saved = await host.Client.PutAsJsonAsync(
            $"/tracon/api/workflows/{StoredApproval}",
            new WorkflowSaveRequest
            {
                Kind = WorkflowKind.Sequential,
                AgentNames = [Agent],
            });

        saved.EnsureSuccessStatusCode();

        return host;
    }

    /// <summary>Runs the workflow until it waits and reads the one request it published.</summary>
    private static async Task<WaitingRun> RunToApprovalAsync(TraconTestHost host, string workflow)
    {
        using var started = await host.Client.PostAsJsonAsync(
            $"/tracon/api/workflows/{workflow}/run",
            new WorkflowRunHttpRequest { Message = "Episode 12" });

        started.StatusCode.ShouldBe(HttpStatusCode.OK);

        var runId = await ReadRunIdAsync(started);
        var run = (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId)).ShouldNotBeNull();

        if (run.Status != RunStatus.AwaitingInput)
        {
            return new WaitingRun(run, string.Empty, string.Empty, string.Empty);
        }

        using var listed = await host.Client.GetAsync(new Uri($"/tracon/api/workflows/runs/{runId}/requests", UriKind.Relative));
        var request = (await TraconTestHost.ReadJsonAsync(listed)).EnumerateArray().ShouldHaveSingleItem();

        return new WaitingRun(
            run,
            request.GetProperty("requestId").GetString()!,
            request.GetProperty("form").GetString()!,
            request.GetProperty("prompt").GetString() ?? string.Empty);
    }

    /// <summary>Runs the workflow and waits for its row to settle, as a run that needs no answer does.</summary>
    private static async Task<RunRecord> RunToCompletionAsync(TraconTestHost host, string workflow)
    {
        using var started = await host.Client.PostAsJsonAsync(
            $"/tracon/api/workflows/{workflow}/run",
            new WorkflowRunHttpRequest { Message = "Episode 13" });

        started.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await host.WaitForTerminalRunAsync(await ReadRunIdAsync(started));
    }

    /// <summary>The run id the first frame of a streamed response names.</summary>
    private static async Task<Guid> ReadRunIdAsync(HttpResponseMessage response)
    {
        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        return JsonDocument.Parse(frames[0].Data).RootElement.GetProperty("runId").GetGuid();
    }

    private static Task<HttpResponseMessage> RespondAsync(TraconTestHost host, WaitingRun waiting, object answer)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["requestId"] = JsonSerializer.SerializeToElement(waiting.RequestId),
        };

        foreach (var property in JsonSerializer.SerializeToElement(answer).EnumerateObject())
        {
            fields[property.Name] = property.Value;
        }

        return host.Client.PostAsJsonAsync($"/tracon/api/workflows/runs/{waiting.Run.Id}/respond", fields);
    }

    private static string TenantId(TraconTestHost host)
        => host.Services.GetRequiredService<ITenantContext>().TenantId;

    private static async Task<int> CountRunsAsync(TraconTestHost host)
        => (await host.Services.GetRequiredService<IRunStore>()
            .QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 200 })).Count;

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

    /// <summary>A workflow run that stopped, and the request it published.</summary>
    private sealed record WaitingRun(RunRecord Run, string RequestId, string Form, string Prompt);

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
