using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>Phase 117: the MCP Tasks extension, backed by AgentPrism's run store.</summary>
public sealed class McpTasksEndpointTests
{
    [Fact]
    public async Task Discover_probe_negotiates_2026_07_28_even_when_the_first_response_is_slow()
    {
        // F-190: McpClient.CreateAsync (ProtocolVersion left unset) first
        // probes with server/discover, bounded by the SDK's own
        // McpClientOptions.DiscoverProbeTimeout -- 5 seconds by production
        // default. Miss that window and the client silently falls back to
        // the legacy `initialize` handshake, negotiating 2025-11-25, which
        // GetTaskAsync then rejects. The full-package CI run hit this rarely
        // under CPU contention (three tests, seen once); this test forces
        // the same slow-first-response shape deterministically instead of
        // waiting for real contention to reproduce it.
        var firstRequest = 0;

        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(o =>
                {
                    o.ExposedAgents.Add("kod-agent");
                    o.EnableTasks = true;
                }),
            configureApp: app => app.Use(async (HttpContext context, Func<Task> next) =>
            {
                if (context.Request.Path.StartsWithSegments("/agentprism/mcp", StringComparison.Ordinal)
                    && Interlocked.Exchange(ref firstRequest, 1) == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(6));
                }

                await next();
            }),
            configureAfterMap: app => app.MapAgentPrismMcpServer());

        await using var client = await McpTaskTestClient.ConnectAsync(host.Client, "/agentprism/mcp");

        client.NegotiatedProtocolVersion.ShouldBe("2026-07-28");
    }

    [Fact]
    public async Task EnableTasks_false_still_answers_synchronously()
    {
        // Default: EnableTasks is off. Even a task-aware client (declares the
        // capability, prefers 2026-07-28) gets today's immediate result — the
        // SDK's own ExecutionModeSelector fast path, never CreateTaskAsync.
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(o => o.ExposedAgents.Add("kod-agent")),
            configureAfterMap: app => app.MapAgentPrismMcpServer());

        await using var client = await McpTaskTestClient.ConnectAsync(host.Client, "/agentprism/mcp");

        var outcome = await client.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "agentprism_kod-agent", Arguments = Arguments("hello") });

        outcome.IsTask.ShouldBeFalse();
        outcome.Result.ShouldNotBeNull();
    }

    [Fact]
    public async Task EnableTasks_false_still_resolves_the_real_tenant_when_tenancy_is_on()
    {
        // CatalogToolCallHandler now wraps every call — task-mode or not — in
        // AmbientTenantScope.Begin(...), and AmbientTenantScope.Current wins
        // over HTTP-header resolution whenever it is set (HttpTenantContext's
        // own precedence). If the provisioning filter ever read the wrong
        // tenant on the SYNCHRONOUS path, this would silently misattribute
        // every run regardless of EnableTasks — an independent-review finding that
        // the synchronous path was never tested under tenancy.
        const string TenantHeader = "X-AgentPrism-Tenant";

        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .UseTenancy(o =>
                {
                    o.Enabled = true;
                    o.AllowHeaderResolution = true;
                })
                .UseMcpServer(o => o.ExposeAllAgents = true),
            configureAfterMap: app => app.MapAgentPrismMcpServer());

        using var createRequest = new HttpRequestMessage(
            HttpMethod.Post, new Uri("/agentprism/api/agents", UriKind.Relative))
        {
            Content = JsonContent.Create(TestData.Request()),
        };
        createRequest.Headers.Add(TenantHeader, "tenant-a");

        using var createResponse = await host.Client.SendAsync(createRequest);
        createResponse.EnsureSuccessStatusCode();

        await using var client = await McpTaskTestClient.ConnectAsync(host.Client, "/agentprism/mcp", tenantHeader: "tenant-a");

        var outcome = await client.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "agentprism_db-agent", Arguments = Arguments("hello") });

        outcome.IsTask.ShouldBeFalse();

        var runs = host.Services.GetRequiredService<IRunStore>();
        var all = await runs.QueryRunsAsync(new RunQuery { TenantId = "tenant-a" });

        all.ShouldHaveSingleItem().AgentName.ShouldBe("db-agent");
    }

    [Fact]
    public async Task EnableTasks_true_creates_a_task_whose_id_is_the_run_id()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(o =>
                {
                    o.ExposedAgents.Add("kod-agent");
                    o.EnableTasks = true;
                }),
            configureAfterMap: app => app.MapAgentPrismMcpServer());

        await using var client = await McpTaskTestClient.ConnectAsync(host.Client, "/agentprism/mcp");

        var outcome = await client.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "agentprism_kod-agent", Arguments = Arguments("hello") });

        outcome.IsTask.ShouldBeTrue();
        var taskId = outcome.TaskCreated!.TaskId;
        Guid.TryParse(taskId, out var runId).ShouldBeTrue();

        var runs = host.Services.GetRequiredService<IRunStore>();
        var run = await Poll(() => runs.GetRunAsync(runId), r => r is not null);

        run!.Id.ShouldBe(runId);
        run.AgentName.ShouldBe("kod-agent");
    }

    [Fact]
    public async Task Tasks_get_transitions_from_working_to_completed_with_the_run_output()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(o =>
                {
                    o.ExposedAgents.Add("kod-agent");
                    o.EnableTasks = true;
                }),
            configureAfterMap: app => app.MapAgentPrismMcpServer());

        await using var client = await McpTaskTestClient.ConnectAsync(host.Client, "/agentprism/mcp");

        var outcome = await client.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "agentprism_kod-agent", Arguments = Arguments("hello") });

        var taskId = outcome.TaskCreated!.TaskId;

        var final = await Poll(
            () => client.GetTaskAsync(taskId),
            r => r is CompletedTaskResult or FailedTaskResult);

        var completed = final.ShouldBeOfType<CompletedTaskResult>();
        DeserializeResult(completed).Content[0].ShouldBeOfType<TextContentBlock>().Text.ShouldContain("hello", Case.Sensitive);

        var runs = host.Services.GetRequiredService<IRunStore>();
        (await runs.GetRunAsync(Guid.Parse(taskId)))!.Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task Tasks_cancel_of_a_running_task_actually_cancels_the_in_flight_tool_call()
    {
        // 🚨 A Task.Delay-based approximation would be test theater (same
        // rationale as ConcurrentToolInvocationTests's Barrier): the tool
        // blocks on a token that is NEVER cancelled except by a real
        // tasks/cancel reaching it, and releases a semaphore on entry so the
        // test knows the call is genuinely in flight before it cancels.
        // Cancellation here flows entirely through the SDK's own per-task
        // CancellationTokenSource (linked into RunRecordingAgent's own
        // registered source) — RunBackedMcpTaskStore.SetCancelledAsync's
        // Running branch does not itself need to touch
        // IRunCancellationRegistry, and this proves that chain end to end
        // rather than assuming it.
        using var entered = new SemaphoreSlim(0, 1);

        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddTool(
                    (Func<CancellationToken, Task<string>>)(async ct =>
                    {
                        entered.Release();
                        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                        return "unreachable";
                    }),
                    name: "block_forever",
                    description: "Blocks until cancelled.")
                .AddModelProvider(new AgentPrism.Testing.FakeModelProvider("routing")
                    .ForModel("blocking-model", cfg => cfg.CallsTool("block_forever")))
                .AddAgent(TestData.Definition() with
                {
                    Name = "kod-agent",
                    Model = new ModelBinding { Provider = "routing", Model = "blocking-model" },
                    ToolNames = ["block_forever"],
                })
                .UseMcpServer(o =>
                {
                    o.ExposedAgents.Add("kod-agent");
                    o.EnableTasks = true;
                }),
            configureAfterMap: app => app.MapAgentPrismMcpServer());

        await using var client = await McpTaskTestClient.ConnectAsync(host.Client, "/agentprism/mcp");

        var outcome = await client.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "agentprism_kod-agent", Arguments = Arguments("go") });

        var taskId = outcome.TaskCreated!.TaskId;

        (await entered.WaitAsync(TimeSpan.FromSeconds(10))).ShouldBeTrue("the tool call never started");

        var runs = host.Services.GetRequiredService<IRunStore>();
        (await runs.GetRunAsync(Guid.Parse(taskId)))!.Status.ShouldBe(RunStatus.Running);

        await client.CancelTaskAsync(taskId);

        var final = await Poll(() => client.GetTaskAsync(taskId), r => r is not WorkingTaskResult);

        final.ShouldBeOfType<CancelledTaskResult>();
        (await runs.GetRunAsync(Guid.Parse(taskId)))!.Status.ShouldBe(RunStatus.Canceled);
    }

    [Fact]
    public async Task Tasks_cancel_of_a_queued_task_closes_the_row_directly()
    {
        // The other branch of SetCancelledAsync: a task-mode call whose
        // background execution has not reached agent.RunAsync yet (nothing is
        // registered in IRunCancellationRegistry for it). Mirrors
        // RunEndpoints.CancelRunAsync's own queued-run branch.
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(o =>
                {
                    o.ExposedAgents.Add("kod-agent");
                    o.EnableTasks = true;
                }),
            configureAfterMap: app => app.MapAgentPrismMcpServer());

        var runs = host.Services.GetRequiredService<IRunStore>();
        var runId = AgentPrismId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "kod-agent",
            Status = RunStatus.Queued,
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = "default",
        });

        await using var client = await McpTaskTestClient.ConnectAsync(host.Client, "/agentprism/mcp");

        await client.CancelTaskAsync(runId.ToString());

        (await runs.GetRunAsync(runId))!.Status.ShouldBe(RunStatus.Canceled);
    }

    [Fact]
    public async Task Unknown_task_id_is_reported_as_a_protocol_error_not_a_500()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(o =>
                {
                    o.ExposedAgents.Add("kod-agent");
                    o.EnableTasks = true;
                }),
            configureAfterMap: app => app.MapAgentPrismMcpServer());

        await using var client = await McpTaskTestClient.ConnectAsync(host.Client, "/agentprism/mcp");

        var exception = await Should.ThrowAsync<McpException>(
            () => client.GetTaskAsync(Guid.NewGuid().ToString()).AsTask());

        exception.Message.ShouldContain("Unknown task", Case.Sensitive);
    }

    [Fact]
    public async Task Approval_requiring_tool_added_after_exposure_rejects_the_task_with_todays_inline_message()
    {
        // K-103, moved to the task-mode path (Phase 117 § 117.4). The startup
        // guard (ExternalSurfaceGuard) only catches a tool present AT expose
        // time; a tool added afterward reaches CatalogToolCallHandler's own
        // runtime check, unchanged, whether the call is synchronous or
        // running as a task. The tool is registered in code (required,
        // AGENTS.md), but the AGENT's tool list is a database-origin
        // definition — exposed empty first, then updated through the
        // management API after the host is already serving MCP requests,
        // exactly reproducing the "dynamic catalog" scenario
        // CatalogToolCallHandler's own comment describes.
        const string ApprovalModel = "approval-model";

        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder =>
            {
                builder
                    .AddTool(
                        (Func<string, string>)(orderId => $"canceled: {orderId}"),
                        name: "cancel_order",
                        description: "Cancels an order.",
                        configure: options => options.RequiresApproval = true)
                    .AddModelProvider(new AgentPrism.Testing.FakeModelProvider("routing")
                        .ForModel(ApprovalModel, cfg => cfg.CallsTool("cancel_order", new { orderId = "123" })))
                    .UseMcpServer(o =>
                    {
                        o.ExposedAgents.Add("db-agent");
                        o.EnableTasks = true;
                    });
            },
            configureAfterMap: app => app.MapAgentPrismMcpServer());

        var createResponse = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/agents", UriKind.Relative),
            TestData.Request() with { Model = new ModelBinding { Provider = "routing", Model = ApprovalModel } });
        createResponse.EnsureSuccessStatusCode();

        var updateResponse = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/agents/db-agent", UriKind.Relative),
            TestData.Request() with
            {
                Model = new ModelBinding { Provider = "routing", Model = ApprovalModel },
                ToolNames = ["cancel_order"],
            });
        updateResponse.EnsureSuccessStatusCode();

        await using var client = await McpTaskTestClient.ConnectAsync(host.Client, "/agentprism/mcp");

        var outcome = await client.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "agentprism_db-agent", Arguments = Arguments("cancel my order") });

        var taskId = outcome.TaskCreated!.TaskId;

        var final = await Poll(() => client.GetTaskAsync(taskId), r => r is CompletedTaskResult or FailedTaskResult);

        // 🚨 Never InputRequired — the actual K-103 safety property.
        final.ShouldNotBeOfType<InputRequiredTaskResult>();

        var completed = final.ShouldBeOfType<CompletedTaskResult>();
        var text = DeserializeResult(completed).Content[0].ShouldBeOfType<TextContentBlock>().Text;
        text.ShouldContain("requires user approval", Case.Sensitive);
        text.ShouldContain("cannot respond to an approval request", Case.Sensitive);
    }

    private static Dictionary<string, JsonElement> Arguments(string message)
        => new(StringComparer.Ordinal) { ["message"] = JsonSerializer.SerializeToElement(message) };

    private static CallToolResult DeserializeResult(CompletedTaskResult result)
        => JsonSerializer.Deserialize<CallToolResult>(result.Result, McpJsonUtilities.DefaultOptions)!;

    private static async Task<T> Poll<T>(Func<Task<T>> read, Func<T, bool> isDone)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (true)
        {
            var value = await read();

            if (isDone(value))
            {
                return value;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Timed out waiting for the task to reach the expected state.");
            }

            await Task.Delay(20);
        }
    }

    private static async Task<T> Poll<T>(Func<ValueTask<T>> read, Func<T, bool> isDone)
        => await Poll(() => read().AsTask(), isDone);
}
