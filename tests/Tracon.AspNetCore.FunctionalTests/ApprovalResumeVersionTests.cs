using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// Same alias reason as ApprovalEndpointTests (K-269): a blanket
// `using Tracon.Testing;` collides with this project's TraconTestHost.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The continuation a tool-approval decision queues runs on the agent version
/// and experiment arm of the run that asked for approval (F-352).
/// </summary>
/// <remarks>
/// 🚨 The approval-resume job resolved the agent's CURRENT version and passed
/// no version or arm. A definition saved while the approval waited changed the
/// instructions halfway through the tool call, and the continuation fell out
/// of the experiment arm its first half counted in. The agent is created over
/// HTTP: a code agent has no versions to diverge.
/// </remarks>
public sealed class ApprovalResumeVersionTests
{
    private const string AgentName = "versioned-approval-agent";
    private const string SessionId = "approval-version-session";

    private static readonly Uri Agents = new("/tracon/api/agents", UriKind.Relative);
    private static readonly Uri Agent = new($"/tracon/api/agents/{AgentName}", UriKind.Relative);
    private static readonly Uri Run = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);
    private static readonly Uri PendingApprovals = new("/tracon/api/approvals/pending", UriKind.Relative);

    [Fact]
    public async Task An_approved_tool_call_resumes_on_the_version_that_asked_for_it()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        await SaveVersionAsync(host, "v1", create: true);

        var sourceRunId = await QueueRunAwaitingApprovalAsync(host);

        // Saved while the approval waits: version 2 is now current.
        await SaveVersionAsync(host, "v2", create: false);

        var resumed = await DecideAndWaitForResumedRunAsync(host, sourceRunId);

        resumed.Status.ShouldBe(RunStatus.Completed);
        resumed.AgentVersion.ShouldBe(1);
        provider.Requests[^1].Options!.Instructions.ShouldBe("v1");
    }

    [Fact]
    public async Task An_approved_tool_call_resumes_in_the_experiment_arm_of_the_run_that_asked_for_it()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        await SaveVersionAsync(host, "v1", create: true);
        await SaveVersionAsync(host, "v2", create: false);

        var experimentUri = new Uri("/tracon/api/experiments/approval-arm", UriKind.Relative);

        using (var created = await host.Client.PutAsJsonAsync(
            experimentUri,
            new ExperimentSaveRequest
            {
                AgentName = AgentName,
                Variants = [new ExperimentVariant { Name = "control", Version = 1, Weight = 100 }],
            }))
        {
            created.EnsureSuccessStatusCode();
        }

        using var started = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/experiments/approval-arm/start", UriKind.Relative), new { });
        started.EnsureSuccessStatusCode();

        var experimentId = (await TraconTestHost.ReadJsonAsync(started)).GetProperty("id").GetGuid();

        var sourceRunId = await QueueRunAwaitingApprovalAsync(host);

        var source = (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(sourceRunId)).ShouldNotBeNull();
        source.ExperimentId.ShouldBe(experimentId);

        var resumed = await DecideAndWaitForResumedRunAsync(host, sourceRunId);

        resumed.Status.ShouldBe(RunStatus.Completed);
        resumed.AgentVersion.ShouldBe(1);
        resumed.ExperimentId.ShouldBe(experimentId);
        resumed.Variant.ShouldBe("control");
        provider.Requests[^1].Options!.Instructions.ShouldBe("v1");
    }

    /// <summary>
    /// Phase 248: the agent was deleted and created again while the approval waited. The numbering used to
    /// restart at 1, so the recorded version 1 named the NEW agent and the continuation ran its text in the
    /// place of the instructions that asked for the approval.
    /// </summary>
    [Fact]
    public async Task An_approved_tool_call_does_not_resume_on_an_agent_created_again_under_the_same_name()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        await SaveVersionAsync(host, "v1", create: true);

        var sourceRunId = await QueueRunAwaitingApprovalAsync(host);

        using (var deleted = await host.Client.DeleteAsync(Agent))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        // The new agent is version 2: version 1 names nothing.
        await SaveVersionAsync(host, "recreated", create: true);

        var requestsBefore = provider.Requests.Count;
        var resumed = await DecideAndWaitForResumedRunAsync(host, sourceRunId);

        resumed.Status.ShouldBe(RunStatus.Failed);
        provider.Requests.Skip(requestsBefore).ShouldNotContain(
            static request => request.Options!.Instructions == "recreated",
            "the continuation never ran the new agent's instructions");
    }

    /// <summary>
    /// A schedule can enqueue an ApprovalResume job with a hand-written
    /// payload; the source run's version is pinned only for the agent that
    /// run ran.
    /// </summary>
    [Fact]
    public async Task A_resume_job_naming_another_agent_than_the_source_run_fails_closed()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        await SaveVersionAsync(host, "v1", create: true);

        // A real agent with the same tool, so only the cross-check refuses it.
        using (var other = await host.Client.PostAsJsonAsync(
            Agents,
            new AgentDefinitionRequest
            {
                Name = "another-agent",
                Instructions = "other",
                Model = new ModelBinding { Provider = "approval-model", Model = "approval-1" },
                ToolNames = ["cancel_order"],
            }))
        {
            other.EnsureSuccessStatusCode();
        }

        var sourceRunId = await QueueRunAwaitingApprovalAsync(host);

        var approvals = host.Services.GetRequiredService<IPendingApprovalStore>();
        var pending = (await approvals.ListPendingAsync()).ShouldHaveSingleItem();
        (await approvals.DecideAsync(pending.Id, approved: true, "tester", DateTimeOffset.UtcNow)).ShouldBeTrue();

        var runs = host.Services.GetRequiredService<IRunStore>();
        var jobs = host.Services.GetRequiredService<IJobStore>();
        var tenantId = host.Services.GetRequiredService<ITenantContext>().TenantId;
        var resumeRunId = TraconId.NewId();
        var now = DateTimeOffset.UtcNow;
        var requestsBefore = provider.Requests.Count;

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = resumeRunId,
            AgentName = "another-agent",
            Status = RunStatus.Queued,
            StartedAt = now,
            TenantId = tenantId,
            SessionId = SessionId,
        });

        await jobs.EnqueueAsync(
            new JobRecord
            {
                Id = resumeRunId,
                TenantId = tenantId,
                HandlerKey = JobHandlerKeys.ApprovalResume,
                TargetName = "another-agent",
                Status = JobStatus.Pending,
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                {
                    runId = resumeRunId.ToString(),
                    approvalId = pending.Id.ToString(),
                    agentName = "another-agent",
                }),
                MaxAttempts = 1,
                ScheduledFor = now,
                CreatedAt = now,
            },
            []);

        var resumed = await host.WaitForTerminalRunAsync(resumeRunId);

        resumed.Status.ShouldBe(RunStatus.Failed);
        resumed.Error.ShouldNotBeNull().Message.ShouldContain("ran a different agent", Case.Sensitive);
        provider.Requests.Count.ShouldBe(requestsBefore);
        (await runs.GetRunAsync(sourceRunId)).ShouldNotBeNull().Status.ShouldBe(RunStatus.AwaitingApproval);
    }

    private static FakeModelProvider Provider()
        => new FakeModelProvider("approval-model")
            .CallsTool("cancel_order", new { orderId = "ORD-7" })
            .EchoesLastToolResult();

    private static Task<TraconTestHost> StartAsync(FakeModelProvider provider)
        => TraconTestHost.StartAsync(
            builder => builder
                .AddModelProvider(provider)
                .AddTool(
                    (Func<string, string>)(orderId => $"{orderId} canceled."),
                    name: "cancel_order",
                    description: "Cancels an order.",
                    configure: options => options.RequiresApproval = true),
            configureServices: static services => services.UseScheduling(
                static options => options.PollInterval = TimeSpan.FromMilliseconds(20)));

    private static async Task SaveVersionAsync(TraconTestHost host, string instructions, bool create)
    {
        var request = new AgentDefinitionRequest
        {
            Name = AgentName,
            Instructions = instructions,
            Model = new ModelBinding { Provider = "approval-model", Model = "approval-1" },
            ToolNames = ["cancel_order"],
        };

        using var response = create
            ? await host.Client.PostAsJsonAsync(Agents, request)
            : await host.Client.PutAsJsonAsync(Agent, request);

        response.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> QueueRunAwaitingApprovalAsync(TraconTestHost host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Run)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "cancel the order", SessionId = SessionId }),
        };
        request.Headers.Add("Prefer", "respond-async");

        using var accepted = await host.Client.SendAsync(request);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        await host.WaitForRunStatusAsync(runId, "AwaitingApproval");

        return runId;
    }

    private static async Task<RunRecord> DecideAndWaitForResumedRunAsync(TraconTestHost host, Guid sourceRunId)
    {
        using var pendingResponse = await host.Client.GetAsync(PendingApprovals);
        var approvalId = (await TraconTestHost.ReadJsonAsync(pendingResponse))
            .EnumerateArray().ShouldHaveSingleItem().GetProperty("id").GetGuid();

        using var decided = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/approvals/{approvalId}/decide", UriKind.Relative),
            new ApprovalDecisionRequest { Approved = true });
        decided.StatusCode.ShouldBe(HttpStatusCode.OK);

        var runs = host.Services.GetRequiredService<IRunStore>();

        var resumed = await WaitUntil.ValueAsync(
            async () => (await runs.QueryRunsAsync(new RunQuery { SessionId = SessionId, OnlyRootRuns = false }))
                .FirstOrDefault(record => record.Id != sourceRunId),
            static record => record is { Status: RunStatus.Completed or RunStatus.Failed },
            $"the run resuming '{sourceRunId}' to settle");

        return resumed.ShouldNotBeNull();
    }
}
