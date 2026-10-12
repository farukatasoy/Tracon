using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// K-269: Tracon.Testing.TraconTestHost and this project's own host share a
// name, so the package is imported one type at a time.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The work a queued run starts, and the run that resumes it after an approval
/// decision, carry the user and the labels of the run they belong to (F-369).
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The queued-run worker opened its attribution scope with the user only, so
/// the run's sub-agents and tools saw no labels; the approval-resume worker
/// opened no scope at all, so the resumed turn's tools and sub-agents saw no
/// user. The runs rows still looked right: the placeholder row carries both,
/// and the store keeps them.
/// </para>
/// <para>
/// The attribution context answers inside a request and reports the ambient
/// scope outside one, the shape a claims-bound context takes when it supports
/// background work (K-692). The cases run through the real host: the value
/// crosses the HTTP request, the job payload and the worker.
/// </para>
/// </remarks>
public sealed class QueuedRunAttributionTests
{
    private const string SessionId = "attributed-session";

    // Microsoft Agent Framework's background task tools.
    private const string StartTask = "background_agents_start_task";
    private const string WaitForCompletion = "background_agents_wait_for_first_completion";
    private const string GetResults = "background_agents_get_task_results";

    private static readonly IReadOnlyDictionary<string, string> Payments =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["team"] = "payments" };

    [Fact]
    public async Task A_queued_runs_sub_agent_carries_the_user_and_labels_the_request_resolved()
    {
        await using var host = await StartAsync(builder =>
        {
            builder.AddModelProvider(new FakeModelProvider("routing")
                .ForModel("router-model", static cfg => cfg
                    .CallsTool(StartTask, new { agentName = "helper", input = "sub task", description = "sub task" })
                    .CallsTool(WaitForCompletion, new { taskIds = new[] { 1 } })
                    .CallsTool(GetResults, new { taskId = 1 })
                    .EchoesLastToolResult("Delegated: "))
                .ForModel("helper-model", static cfg => cfg.RespondsWith("Sub task done")));

            builder.AddAgent(new AgentDefinition
            {
                Name = "helper",
                Description = "Does the sub task.",
                Instructions = "Help.",
                Model = new ModelBinding { Provider = "routing", Model = "helper-model" },
                Origin = AgentDefinitionOrigin.Code,
            });

            builder.AddAgent(new AgentDefinition
            {
                Name = "router",
                Description = "Delegates the work.",
                Instructions = "Delegate.",
                Model = new ModelBinding { Provider = "routing", Model = "router-model" },
                CallableAgentNames = ["helper"],
                Origin = AgentDefinitionOrigin.Code,
            });
        });

        var rootId = await QueueAsync(host, "router");
        (await host.WaitForTerminalRunAsync(rootId)).Status.ShouldBe(RunStatus.Completed);

        var runs = await host.Services.GetRequiredService<IRunStore>()
            .QueryRunsAsync(new RunQuery { OnlyRootRuns = false });

        var child = runs.Single(static run => run.Depth == 1);

        child.ParentRunId.ShouldBe(rootId);
        child.UserId.ShouldBe("ada");
        child.Labels.ShouldNotBeNull().ShouldContainKeyAndValue("team", "payments");
    }

    [Fact]
    public async Task An_approval_resume_runs_under_the_user_and_labels_of_the_run_that_asked()
    {
        AgentRunScope? seen = null;

        await using var host = await StartAsync(builder => builder
            .AddModelProvider(new FakeModelProvider("approval-model")
                .CallsTool("cancel_order", new { orderId = "ORD-7" })
                .EchoesLastToolResult())
            .AddTool(
                (Func<string, string>)(orderId =>
                {
                    // Runs only after the decision, inside the resumed run.
                    seen = TraconRunContext.Current;

                    return $"{orderId} canceled.";
                }),
                name: "cancel_order",
                description: "Cancels an order.",
                configure: static options => options.RequiresApproval = true)
            .AddAgent(new AgentDefinition
            {
                Name = "approver",
                Instructions = "Give a short answer.",
                Model = new ModelBinding { Provider = "approval-model", Model = "approval-1" },
                ToolNames = ["cancel_order"],
            }));

        var sourceRunId = await QueueAsync(host, "approver");
        await host.WaitForRunStatusAsync(sourceRunId, "AwaitingApproval");

        using (var pending = await host.Client.GetAsync(new Uri("/tracon/api/approvals/pending", UriKind.Relative)))
        {
            var approvalId = (await TraconTestHost.ReadJsonAsync(pending))
                .EnumerateArray().ShouldHaveSingleItem().GetProperty("id").GetGuid();

            using var decided = await host.Client.PostAsJsonAsync(
                new Uri($"/tracon/api/approvals/{approvalId}/decide", UriKind.Relative),
                new ApprovalDecisionRequest { Approved = true });

            decided.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var runs = host.Services.GetRequiredService<IRunStore>();

        var resumed = await WaitUntil.ValueAsync(
            async () => (await runs.QueryRunsAsync(new RunQuery { SessionId = SessionId }))
                .FirstOrDefault(record => record.Id != sourceRunId),
            static record => record is { Status: RunStatus.Completed or RunStatus.Failed },
            "the resumed run to settle");

        resumed!.Status.ShouldBe(RunStatus.Completed, resumed.Error?.Message);

        seen.ShouldNotBeNull();
        seen.RunId.ShouldBe(resumed.Id);
        seen.UserId.ShouldBe("ada");
        seen.Labels.ShouldNotBeNull().ShouldContainKeyAndValue("team", "payments");
    }

    private static Task<TraconTestHost> StartAsync(Action<ITraconBuilder> configureTracon)
        => TraconTestHost.StartAsync(
            configureTracon,
            configureServices: static services =>
            {
                services.AddHttpContextAccessor();

                // Registered BEFORE Tracon's own TryAdd, the way a consumer
                // binding its identity pipeline would.
                services.AddSingleton<IRunAttributionContext>(static provider =>
                    new RequestBoundAttribution(provider.GetRequiredService<IHttpContextAccessor>()));

                services.UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(20));
            });

    private static async Task<Guid> QueueAsync(TraconTestHost host, string agentName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{agentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "start", SessionId = SessionId }),
        };
        request.Headers.Add("Prefer", "respond-async");

        using var accepted = await host.Client.SendAsync(request);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        return (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();
    }

    /// <summary>
    /// Answers from the request, the way a claims-bound context does. Outside a
    /// request (the worker) it reports the ambient scope Tracon opens from the job.
    /// </summary>
    private sealed class RequestBoundAttribution(IHttpContextAccessor accessor) : IRunAttributionContext
    {
        public string? UserId => accessor.HttpContext is null ? AmbientRunAttributionScope.CurrentUserId : "ada";

        public IReadOnlyDictionary<string, string>? Labels
            => accessor.HttpContext is null ? AmbientRunAttributionScope.CurrentLabels : Payments;
    }
}
