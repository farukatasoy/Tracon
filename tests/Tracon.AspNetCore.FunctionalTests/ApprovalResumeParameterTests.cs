using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A tool-approval decision resumes a parameterized queued run under the
/// instructions that asked for the approval.
/// </summary>
/// <remarks>
/// Once a queued run can carry values, the approval-resume job -
/// which resolves the agent from the catalog - would answer the tool call on the
/// agent's DEFAULTS, a different prompt from the one that requested it. The
/// resume reads the source run's recorded values instead, and refuses when it
/// has none (input recording off, or a run from before parameter recording)
/// rather than guess.
/// </remarks>
public sealed class ApprovalResumeParameterTests
{
    private const string AgentName = "parameterized-approval-agent";
    private const string SessionId = "approval-parameter-session";

    [Fact]
    public async Task An_approved_tool_call_resumes_with_the_values_the_queued_run_was_given()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider, recordRunInput: true);

        var sourceRunId = await QueueRunAwaitingApprovalAsync(host, "Acme");

        provider.Requests[^1].Options!.Instructions.ShouldBe("Serve Acme.");

        var resumed = await DecideAndWaitForResumedRunAsync(host, sourceRunId);

        resumed.Status.ShouldBe(RunStatus.Completed, resumed.Error?.Message);

        // The default is 'friend'; the resume is 'Acme'.
        provider.Requests[^1].Options!.Instructions.ShouldBe("Serve Acme.");

        var tenant = host.Services.GetRequiredService<ITenantContext>().TenantId;
        var record = await host.Services.GetRequiredService<IRunInputStore>().GetAsync(tenant, resumed.Id);

        record.ShouldNotBeNull();
        record.Parameters.ShouldNotBeNull().ShouldContainKeyAndValue("customer", "Acme");
    }

    [Fact]
    public async Task A_source_run_that_recorded_nothing_cannot_be_resumed_on_the_defaults()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider, recordRunInput: false);

        var sourceRunId = await QueueRunAwaitingApprovalAsync(host, "Acme");
        var requestsBefore = provider.Requests.Count;

        var resumed = await DecideAndWaitForResumedRunAsync(host, sourceRunId);

        resumed.Status.ShouldBe(RunStatus.Failed);

        var message = resumed.Error.ShouldNotBeNull().Message;

        message.ShouldContain("did not record the values", Case.Sensitive);
        message.ShouldContain("RecordRunInput", Case.Sensitive);
        provider.Requests.Count.ShouldBe(requestsBefore, "no model call is made under the wrong instructions");
    }

    private static FakeModelProvider Provider()
        => new FakeModelProvider("approval-model")
            .CallsTool("cancel_order", new { orderId = "ORD-7" })
            .EchoesLastToolResult();

    private static async Task<TraconTestHost> StartAsync(FakeModelProvider provider, bool recordRunInput)
    {
        var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder
                .AddModelProvider(provider)
                .AddTool(
                    (Func<string, string>)(orderId => $"{orderId} canceled."),
                    name: "cancel_order",
                    description: "Cancels an order.",
                    configure: options => options.RequiresApproval = true);
                builder.UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(20));
            },
            configureServices: services =>
            {
                services.Configure<TraconOptions>(options => options.RunRecording.RecordRunInput = recordRunInput);
            });

        using var created = await host.Client.PostAsJsonAsync(
            "/tracon/api/agents",
            new AgentDefinitionRequest
            {
                Name = AgentName,
                Instructions = "Serve {{customer}}.",
                Parameters = [new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text, DefaultValue = "friend" }],
                Model = new ModelBinding { Provider = "approval-model", Model = "approval-1" },
                ToolNames = ["cancel_order"],
            });

        created.EnsureSuccessStatusCode();

        return host;
    }

    private static async Task<Guid> QueueRunAwaitingApprovalAsync(TraconTestHost host, string customer)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/agents/{AgentName}/run")
        {
            Content = JsonContent.Create(new AgentRunRequest
            {
                Message = "cancel the order",
                SessionId = SessionId,
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = customer },
            }),
        };

        request.Headers.Add("Prefer", "respond-async");

        using var accepted = await host.Client.SendAsync(request);

        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());

        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        await host.WaitForRunStatusAsync(runId, "AwaitingApproval");

        return runId;
    }

    private static async Task<RunRecord> DecideAndWaitForResumedRunAsync(TraconTestHost host, Guid sourceRunId)
    {
        using var pendingResponse = await host.Client.GetAsync("/tracon/api/approvals/pending");
        var approvalId = (await TraconTestHost.ReadJsonAsync(pendingResponse))
            .EnumerateArray().ShouldHaveSingleItem().GetProperty("id").GetGuid();

        using var decided = await host.Client.PostAsJsonAsync(
            $"/tracon/api/approvals/{approvalId}/decide",
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
