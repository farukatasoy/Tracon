using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A skill script's whole path with the tree's budget in it: the model asks for the script, a person approves
/// the call, the run that continues the answer runs the script, and a continuation whose budget is spent does not.
/// </summary>
/// <remarks>
/// <para>
/// proved the script gate with <c>script.RunAsync</c> called by the test. That skips the three hops the real
/// call makes: Microsoft Agent Framework's own approval of <c>run_skill_script</c>, the queue and the mailbox, and the
/// run that continues the answer. Each of them can lose the run scope the gate reads the budget from. Here nothing is
/// called by hand: the HTTP queue, the approval inbox, the job worker, the model's own call and the registered executor
/// are the real ones.
/// </para>
/// <para>
/// No standing approval rule is registered for <c>run_skill_script</c>, so Microsoft Agent Framework's own approval
/// stops every call, the way a host that did not turn it off behaves.
/// </para>
/// <para>
/// The continuation starts with a budget of its own, and its first act is the approved call: nothing in the run has
/// spent anything yet. The spent budget of the second case is therefore made by a step the host puts in front of the
/// agent (<see cref="ChargesTheContinuation"/>), the way a paid retrieval or a paid guard would charge the tree before
/// the call starts. Letting a sibling tool of the same turn spend instead would depend on the order in which the
/// framework runs the calls of a turn it resumes, which is not a promise.
/// </para>
/// </remarks>
public sealed class SkillScriptBudgetTests
{
    private const string Provider = "script-provider";
    private const string Model = "script-model";
    private const string AgentName = "script-agent";
    private const string SessionId = "script-session";
    private const string SkillName = "budget-skill";
    private const string ScriptName = "hello";

    private static readonly Uri Run = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);
    private static readonly Uri PendingApprovals = new("/tracon/api/approvals/pending", UriKind.Relative);

    [Fact]
    public async Task An_approved_script_runs_in_the_run_that_continues_the_answer()
    {
        var executor = new RecordingScriptExecutor();

        await using var host = await StartAsync(executor, charges: false);

        var first = await QueueAsync(host);

        (await host.WaitForRunStatusAsync(first, "AwaitingApproval")).ShouldBe("AwaitingApproval");

        // Nothing ran while the call waited for the person.
        executor.Requests.ShouldBeEmpty();

        await DecideAsync(host, await SinglePendingApprovalAsync(host));

        var continuation = await WaitForContinuationAsync(host, [first]);

        continuation.Status.ShouldBe(RunStatus.Completed, continuation.Error?.Message);

        var request = executor.Requests.ShouldHaveSingleItem();

        request.SkillName.ShouldBe(SkillName);
        request.ScriptName.ShouldBe(ScriptName);
        request.RunId.ShouldBe(continuation.Id, "the script ran in the continuation, not in the run that asked");

        var trail = await AuditActionsAsync(host);

        trail.ShouldContain("script.run", StringComparer.Ordinal);
        trail.ShouldNotContain("script.denied", StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_continuation_whose_budget_is_spent_does_not_start_the_script_and_the_refusal_is_audited()
    {
        var executor = new RecordingScriptExecutor();

        await using var host = await StartAsync(executor, charges: true, configureOptions: options => options.AgentGraph.MaxTotalCost = 1m);

        var first = await QueueAsync(host);

        (await host.WaitForRunStatusAsync(first, "AwaitingApproval")).ShouldBe("AwaitingApproval");

        await DecideAsync(host, await SinglePendingApprovalAsync(host));

        var continuation = await WaitForContinuationAsync(host, [first]);

        // The refusal keeps the identity of every budget refusal: the run ends at its next model call.
        continuation.Status.ShouldBe(RunStatus.Failed);
        continuation.Error.ShouldNotBeNull().Type.ShouldBe(TraconRunBudgetExceededException.RunBudgetExceededErrorType);

        // The script of a spent tree was not handed to the executor, and the trail says why.
        executor.Requests.ShouldBeEmpty("the script of a spent tree was handed to the executor");

        var denial = (await DeniedEntriesAsync(host)).ShouldHaveSingleItem();

        denial.Entity.ShouldBe($"{SkillName}/{ScriptName}");
        denial.After.ShouldNotBeNull();
        JsonSerializer.Deserialize<string>(denial.After).ShouldNotBeNull().ShouldContain("cost budget is exhausted", Case.Sensitive);
        (await AuditActionsAsync(host)).ShouldNotContain("script.run", StringComparer.Ordinal);
    }

    [Fact]
    public async Task The_run_that_asked_for_the_script_is_not_charged_by_the_step_that_charges_the_continuation()
    {
        // The control of the case above: the same host asks for the script and stops at the approval. The step charges
        // only a run that carries a person's answer, so the first run is untouched and its budget is not spent.
        var executor = new RecordingScriptExecutor();

        await using var host = await StartAsync(executor, charges: true, configureOptions: options => options.AgentGraph.MaxTotalCost = 1m);

        var first = await QueueAsync(host);

        (await host.WaitForRunStatusAsync(first, "AwaitingApproval")).ShouldBe("AwaitingApproval");

        executor.Requests.ShouldBeEmpty();
        (await DeniedEntriesAsync(host)).ShouldBeEmpty();
    }

    private static async Task<TraconTestHost> StartAsync(
        RecordingScriptExecutor executor,
        bool charges,
        Action<TraconOptions>? configureOptions = null)
    {
        var host = await TraconTestHost.StartAsync(
            configureServices: services =>
            {
                services.AddSingleton<ISkillScriptExecutor>(executor);
                services.Configure<TraconOptions>(options =>
                {
                    options.Pricing.Currency = "USD";
                    configureOptions?.Invoke(options);
                });

                if (charges)
                {
                    services.AddSingleton<IAgentDecorator, ChargesTheContinuation>();
                }
            },
            configureTracon: builder =>
            {
                builder
                .UseSkillScripts(options =>
                {
                    options.PlatformIsolationAcknowledged = true;
                    options.AllowStoredScripts = true;
                    options.Interpreters["sh"] = "/bin/sh";
                })
                .AddModelProvider(new FakeModelProvider(Provider)
                    .CallsTool("run_skill_script", new { skillName = SkillName, scriptName = ScriptName })
                    .EchoesLastToolResult("Script said: "))
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Run the script.",
                    Model = new ModelBinding { Provider = Provider, Model = Model },
                    SkillNames = [SkillName],
                });
                builder.UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(20));
            });

        var saved = await host.Services.GetRequiredService<IAgentSkillStore>().SaveAsync(new AgentSkillDefinition
        {
            TenantId = "default",
            Name = SkillName,
            Description = "Skill for the budget tests.",
            Instructions = "Run the script when asked.",
            Scripts = [new AgentSkillScriptDefinition { Name = ScriptName, Extension = "sh", Content = "echo hello" }],
        });

        await host.Services.GetRequiredService<ISkillScriptGrantStore>().GrantAsync(new SkillScriptGrant
        {
            TenantId = "default",
            SkillName = SkillName,
            ContentHash = saved.ScriptSetHash,
            GrantedAt = DateTimeOffset.UtcNow,
        });

        return host;
    }

    private static async Task<Guid> QueueAsync(TraconTestHost host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Run)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "run the script", SessionId = SessionId }),
        };
        request.Headers.Add("Prefer", "respond-async");

        using var accepted = await host.Client.SendAsync(request);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());

        return (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();
    }

    private static async Task<Guid> SinglePendingApprovalAsync(TraconTestHost host)
    {
        using var pending = await host.Client.GetAsync(PendingApprovals);

        var approval = (await TraconTestHost.ReadJsonAsync(pending)).EnumerateArray().ShouldHaveSingleItem();

        approval.GetProperty("toolName").GetString().ShouldBe("run_skill_script");

        return approval.GetProperty("id").GetGuid();
    }

    private static async Task DecideAsync(TraconTestHost host, Guid approvalId)
    {
        using var decided = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/approvals/{approvalId}/decide", UriKind.Relative),
            new ApprovalDecisionRequest { Approved = true });

        decided.StatusCode.ShouldBe(HttpStatusCode.OK, await decided.Content.ReadAsStringAsync());
    }

    /// <summary>Waits for the run of the session that is not one of <paramref name="known"/> to reach a terminal status.</summary>
    private static async Task<RunRecord> WaitForContinuationAsync(TraconTestHost host, Guid[] known)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();

        var run = await WaitUntil.ValueAsync(
            async () => (await runs.QueryRunsAsync(new RunQuery { SessionId = SessionId, OnlyRootRuns = true }))
                .FirstOrDefault(record => !known.Contains(record.Id)),
            static record => record is { Status: RunStatus.Completed or RunStatus.Failed or RunStatus.AwaitingApproval },
            $"the run that continues session '{SessionId}' to settle");

        return run!;
    }

    private static async Task<IReadOnlyList<AuditEntry>> AuditEntriesAsync(TraconTestHost host)
        => await host.Services.GetRequiredService<IAuditLog>().QueryAsync(new AuditQuery { TenantId = "default" });

    private static async Task<IReadOnlyList<string>> AuditActionsAsync(TraconTestHost host)
        => [.. (await AuditEntriesAsync(host)).Select(static entry => entry.Action)];

    private static async Task<IReadOnlyList<AuditEntry>> DeniedEntriesAsync(TraconTestHost host)
        => [.. (await AuditEntriesAsync(host)).Where(static entry => string.Equals(entry.Action, "script.denied", StringComparison.Ordinal))];

    /// <summary>Records every request and answers without starting anything.</summary>
    private sealed class RecordingScriptExecutor : ISkillScriptExecutor
    {
        public ConcurrentQueue<SkillScriptExecutionRequest> Requests { get; } = new();

        public ValueTask<SkillScriptExecutionResult> ExecuteAsync(
            SkillScriptExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Enqueue(request);

            return ValueTask.FromResult(new SkillScriptExecutionResult
            {
                ExitCode = 0,
                StandardOutput = $"routed-to-executor:{request.ScriptName}",
            });
        }
    }

    /// <summary>
    /// A step in front of the agent that charges the tree five units of cost before the model or a tool is reached,
    /// but only for a run that carries a person's answer to an approval request: the run that continues one.
    /// </summary>
    /// <remarks>
    /// It stands for whatever a host puts ahead of the agent that costs money. It runs inside the run recorder (a higher
    /// order wraps closer to the agent), so it charges the budget of the run that is starting, through the ambient scope
    /// the real steps use.
    /// </remarks>
    private sealed class ChargesTheContinuation : IAgentDecorator
    {
        public int Order => 10;

        public AIAgent Decorate(AIAgent agent, AgentDescriptor descriptor) => new Charging(agent);

        private sealed class Charging(AIAgent inner) : DelegatingAIAgent(inner)
        {
            protected override Task<AgentResponse> RunCoreAsync(
                IEnumerable<ChatMessage> messages,
                AgentSession? session = null,
                AgentRunOptions? options = null,
                CancellationToken cancellationToken = default)
            {
                var carried = messages as IReadOnlyList<ChatMessage> ?? [.. messages];

                if (carried.SelectMany(static message => message.Contents).OfType<ToolApprovalResponseContent>().Any())
                {
                    TraconRunContext.Current?.Budget?.RecordUsage(0, 5m);
                }

                return base.RunCoreAsync(carried, session, options, cancellationToken);
            }
        }
    }
}
