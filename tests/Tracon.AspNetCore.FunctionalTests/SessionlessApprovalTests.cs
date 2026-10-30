using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A run with no session that stops at a tool approval can never be answered, so it
/// is closed as <see cref="RunStatus.Failed"/>, on every path that starts one.
/// </summary>
/// <remarks>
/// The queued agent run already had this rule. A batch item and an eval case
/// run without a session too; theirs ended <c>AwaitingApproval</c> with no inbox row
/// to answer, and the batch reported the item <c>Completed</c>. Both run through the
/// real worker, because the status a job reports and the run row are written in two
/// different places.
/// </remarks>
public sealed class SessionlessApprovalTests
{
    private const string AgentName = "refund-agent";
    private const string ModelName = "approval-model";
    private const string Suite = "approval-suite";

    [Fact]
    public async Task A_batch_item_that_stops_at_an_approval_fails_and_its_run_is_closed_as_failed()
    {
        await using var host = await StartAsync();

        using (var saved = await host.Client.PutAsJsonAsync(
                   "/tracon/api/schedules/batch",
                   new JobScheduleSaveRequest
                   {
                       HandlerKey = JobHandlerKeys.AgentBatch,
                       TargetName = AgentName,
                       Payload = JsonDocument.Parse("""["refund order 7"]""").RootElement,
                   }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        using var triggered = await host.Client.PostAsJsonAsync("/tracon/api/schedules/batch/trigger", new { });
        triggered.StatusCode.ShouldBe(HttpStatusCode.OK, await triggered.Content.ReadAsStringAsync());

        var jobId = (await TraconTestHost.ReadJsonAsync(triggered)).GetProperty("id").GetGuid();
        var jobs = host.Services.GetRequiredService<IJobStore>();
        var tenant = host.Services.GetRequiredService<ITenantContext>().TenantId;

        await WaitUntil.ValueAsync(
            async () => (await jobs.GetAsync(tenant, jobId))!,
            static job => job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled,
            "the batch job to finish");

        var item = (await jobs.ListItemsAsync(jobId)).ShouldHaveSingleItem();

        item.Status.ShouldBe(JobItemStatus.Failed, "an item whose run waits for an approval nobody can give did not complete");
        item.Error.ShouldNotBeNull().ShouldContain("approval");

        var run = (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(item.RunId!.Value)).ShouldNotBeNull();
        run.Status.ShouldBe(RunStatus.Failed);
    }

    [Fact]
    public async Task An_eval_case_whose_run_stops_at_an_approval_fails_and_its_run_is_closed_as_failed()
    {
        await using var host = await StartAsync();

        using (var suite = await host.Client.PutAsJsonAsync(
                   $"/tracon/api/evals/{Suite}",
                   new EvalSuiteSaveRequest
                   {
                       AgentName = AgentName,
                       Checks = JsonDocument.Parse("""[{"kind":"nonEmpty","minLength":1}]""").RootElement,
                   }))
        {
            suite.EnsureSuccessStatusCode();
        }

        using (var cases = await host.Client.PutAsJsonAsync(
                   $"/tracon/api/evals/{Suite}/cases",
                   new object[] { new { query = "refund order 7" } }))
        {
            cases.EnsureSuccessStatusCode();
        }

        using var triggered = await host.Client.PostAsJsonAsync($"/tracon/api/evals/{Suite}/run", new { });
        triggered.StatusCode.ShouldBe(HttpStatusCode.OK, await triggered.Content.ReadAsStringAsync());

        var evalRunId = (await TraconTestHost.ReadJsonAsync(triggered)).GetProperty("id").GetGuid();

        var detail = await WaitUntil.ValueAsync(
            async () =>
            {
                using var response = await host.Client.GetAsync($"/tracon/api/evals/runs/{evalRunId}");

                return await TraconTestHost.ReadJsonAsync(response);
            },
            static body => body.GetProperty("run").GetProperty("status").GetString() is "Completed" or "Failed",
            "the eval run to finish");

        var result = detail.GetProperty("results").EnumerateArray().ShouldHaveSingleItem();

        result.GetProperty("passed").GetBoolean().ShouldBeFalse();
        result.GetProperty("failureReason").GetString().ShouldNotBeNull().ShouldContain("approval");

        var runId = result.GetProperty("runId").GetGuid();
        var run = (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId)).ShouldNotBeNull();
        run.Status.ShouldBe(RunStatus.Failed);
    }

    [Fact]
    public async Task A_chat_completion_that_stops_at_an_approval_is_409_and_its_run_is_closed_as_failed()
    {
        // The endpoint answered 200 with empty content, and the run stayed
        // AwaitingApproval with no inbox row: nobody could ever answer it.
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/v1/chat/completions",
            new { model = AgentName, messages = new[] { new { role = "user", content = "refund order 7" } } });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());

        var error = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("error");

        error.GetProperty("type").GetString().ShouldBe("approval_required");
        error.GetProperty("message").GetString().ShouldNotBeNull().ShouldContain("refund_order", Case.Sensitive);

        (await OnlyRunAsync(host, AgentName)).Status.ShouldBe(RunStatus.Failed);
    }

    [Fact]
    public async Task A_streamed_chat_completion_that_stops_at_an_approval_ends_with_an_error_frame_and_its_run_is_closed_as_failed()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/v1/chat/completions",
            new { model = AgentName, stream = true, messages = new[] { new { role = "user", content = "refund order 7" } } });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var frames = body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(static frame => frame.Trim())
            .Where(static frame => frame.StartsWith("data: ", StringComparison.Ordinal))
            .Select(static frame => frame["data: ".Length..])
            .ToList();

        frames.ShouldNotContain(static frame => frame == "[DONE]", "a stream that ends at an approval did not finish");
        frames.ShouldNotContain(static frame => frame.Contains("\"finish_reason\":\"stop\"", StringComparison.Ordinal));

        var error = JsonDocument.Parse(frames[^1]).RootElement.GetProperty("error");
        error.GetProperty("type").GetString().ShouldBe("approval_required");

        (await OnlyRunAsync(host, AgentName)).Status.ShouldBe(RunStatus.Failed);
    }

    [Fact]
    public async Task A_buffered_run_without_a_session_that_stops_at_an_approval_is_409_and_closed_as_failed()
    {
        // Same class: the run endpoint answered 200 and left the run
        // AwaitingApproval with no inbox row, and an approval needs a 'sessionId'.
        await using var host = await StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/agents/{AgentName}/run")
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "refund order 7" }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());

        var problem = await TraconTestHost.ReadJsonAsync(response);
        problem.GetProperty("errorType").GetString().ShouldBe("approval_unanswerable");
        problem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("sessionId", Case.Sensitive);

        (await OnlyRunAsync(host, AgentName)).Status.ShouldBe(RunStatus.Failed);
    }

    [Fact]
    public async Task A_streamed_run_without_a_session_that_stops_at_an_approval_ends_with_an_error_frame()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            $"/tracon/api/agents/{AgentName}/run",
            new AgentRunRequest { Message = "refund order 7" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        frames.ShouldNotContain(static frame => frame.Event == "done");
        frames.ShouldNotContain(static frame => frame.Event == "approvals", "nobody can answer the approval this frame would announce");

        var error = frames[^1];
        error.Event.ShouldBe("error");
        error.Data.ShouldContain("approval_unanswerable", Case.Sensitive);

        (await OnlyRunAsync(host, AgentName)).Status.ShouldBe(RunStatus.Failed);
    }

    [Fact]
    public async Task An_MCP_tool_call_that_stops_at_an_approval_closes_its_run_as_failed()
    {
        // Same class: the caller got the refusal, and the run stayed
        // AwaitingApproval with no inbox row. The tool reaches the agent after it was
        // exposed, the only way past the startup guard.
        await using var host = await StartAsync(
            configureTracon: static builder => builder.UseMcpServer(static o => o.ExposedAgents.Add(ExposedName)),
            configureAfterMap: static app => app.MapTraconMcpServer());

        await ExposeThenAddTheApprovalToolAsync(host);

        var (response, body) = await McpTestClient.SendAsync(
            host.Client,
            "/tracon/mcp",
            "tools/call",
            new { name = $"tracon_{ExposedName}", arguments = new { message = "refund order 7" } });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var result = body!.Value.GetProperty("result");
        result.GetProperty("isError").GetBoolean().ShouldBeTrue();
        result.GetProperty("content")[0].GetProperty("text").GetString().ShouldNotBeNull()
            .ShouldContain("cannot respond to an approval request", Case.Sensitive);

        (await OnlyRunAsync(host, ExposedName)).Status.ShouldBe(RunStatus.Failed);
    }

    [Fact]
    public async Task An_A2A_message_that_stops_at_an_approval_closes_its_run_as_failed()
    {
        await using var host = await StartAsync(
            configureTracon: static builder => builder.UseA2A(static o => o.ExposedAgents.Add(ExposedName)),
            configureAfterMap: static app => app.MapTraconA2A());

        await CreateExposedAgentAsync(host);

        // The startup guard reads the catalog once, when the host starts. The agent is saved after
        // that, so the guard never sees it, and it has no tool yet either.
        using (var card = await host.Client.GetAsync($"/tracon/a2a/{ExposedName}/.well-known/agent-card.json"))
        {
            card.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await AddTheApprovalToolAsync(host);

        var sendMessage = JsonSerializer.SerializeToElement(
            new A2A.SendMessageRequest
            {
                Message = new A2A.Message
                {
                    Role = A2A.Role.User,
                    Parts = [A2A.Part.FromText("refund order 7")],
                    MessageId = Guid.NewGuid().ToString(),
                },
            },
            A2A.A2AJsonUtilities.DefaultOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/a2a/{ExposedName}")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "SendMessage", @params = sendMessage }),
        };
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await host.Client.SendAsync(request);
        var reply = await TraconTestHost.ReadJsonAsync(response);

        // The refusal is a JSON-RPC error. Its text does not name the approval: the A2A
        // host replaces the proxy's message with its own (a separate candidate).
        reply.TryGetProperty("error", out _).ShouldBeTrue(reply.ToString());

        (await OnlyRunAsync(host, ExposedName)).Status.ShouldBe(RunStatus.Failed);
    }

    private const string ExposedName = "db-agent";

    private static async Task ExposeThenAddTheApprovalToolAsync(TraconTestHost host)
    {
        await CreateExposedAgentAsync(host);
        await AddTheApprovalToolAsync(host);
    }

    /// <summary>Creates the exposed agent with no tools: the startup guard lets it through.</summary>
    private static async Task CreateExposedAgentAsync(TraconTestHost host)
    {
        using var created = await host.Client.PostAsJsonAsync(
            "/tracon/api/agents",
            TestData.Request(ExposedName) with { Model = new ModelBinding { Provider = "approval-provider", Model = ModelName } });

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
    }

    private static async Task AddTheApprovalToolAsync(TraconTestHost host)
    {
        using var updated = await host.Client.PutAsJsonAsync(
            $"/tracon/api/agents/{ExposedName}",
            TestData.Request(ExposedName) with
            {
                Model = new ModelBinding { Provider = "approval-provider", Model = ModelName },
                ToolNames = ["refund_order"],
            });

        updated.EnsureSuccessStatusCode();
    }

    private static async Task<RunRecord> OnlyRunAsync(TraconTestHost host, string agentName)
        => (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery { AgentName = agentName }))
            .ShouldHaveSingleItem();

    private static Task<TraconTestHost> StartAsync(
        Action<ITraconBuilder>? configureTracon = null,
        Action<Microsoft.AspNetCore.Builder.WebApplication>? configureAfterMap = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder
                    .AddTool(
                        (Func<string, string>)RefundOrder,
                        name: "refund_order",
                        description: "Refunds an order.",
                        configure: static options => options.RequiresApproval = true)
                    .AddModelProvider(new FakeModelProvider("approval-provider")
                        .WithModel(new ModelDescriptor { Name = ModelName })
                        .CallsTool("refund_order", new { orderId = "7" })
                        .EchoesLastToolResult())
                    .AddAgent(new AgentDefinition
                    {
                        Name = AgentName,
                        Instructions = "Refund the order the user names.",
                        Model = new ModelBinding { Provider = "approval-provider", Model = ModelName },
                        ToolNames = ["refund_order"],
                        Origin = AgentDefinitionOrigin.Code,
                    });

                configureTracon?.Invoke(builder);
                builder.UseScheduling(static options =>
                {
                    options.RunWorker = true;
                    options.PollInterval = TimeSpan.FromMilliseconds(20);
                });
            },
            configureAfterMap: configureAfterMap);

    private static string RefundOrder(string orderId) => $"refunded {orderId}";
}
