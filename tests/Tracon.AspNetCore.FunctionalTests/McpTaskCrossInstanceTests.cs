using System.Net.Http.Json;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 117: <see cref="RunBackedMcpTaskStore"/>'s same-instance cache is a fast
/// path, not the source of truth — a poll landing on a DIFFERENT instance must
/// still resolve correctly straight from the shared database. Two real, separate
/// <see cref="TraconTestHost"/>s sharing one SQLite file (not two in-memory
/// hosts, which would each get their own isolated store) exercise this: task
/// creation and completion happen on host A, every poll happens on host B, whose
/// <see cref="RunBackedMcpTaskStore"/> was never told anything by A — its
/// same-instance cache is empty by construction, so every read here is forced
/// through <see cref="McpTaskStatusMapping"/>'s reconstruction path. This is also
/// the automated proof for the phase's "a task is visible from a second instance"
/// DoD line.
/// </summary>
public sealed class McpTaskCrossInstanceTests
{
    [Fact]
    public async Task Second_instance_reconstructs_a_completed_task_from_the_shared_database()
    {
        using var database = new TempSqliteDatabase("mcp-task-cross-instance");

        await using var hostA = await StartAsync(database.ConnectionString);
        await using var clientA = await McpTaskTestClient.ConnectAsync(hostA.Client, "/tracon/mcp");

        var outcome = await clientA.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "tracon_kod-agent", Arguments = Arguments("hello from host A") });

        var taskId = outcome.TaskCreated!.TaskId;

        // Host A's own cache resolves this once it settles -- proves the
        // task actually completes before we move to a store that has
        // never seen it.
        await Poll(() => clientA.GetTaskAsync(taskId), r => r is CompletedTaskResult);

        await using var hostB = await StartAsync(database.ConnectionString);
        await using var clientB = await McpTaskTestClient.ConnectAsync(hostB.Client, "/tracon/mcp");

        var final = await Poll(() => clientB.GetTaskAsync(taskId), r => r is CompletedTaskResult);

        var completed = final.ShouldBeOfType<CompletedTaskResult>();
        var result = JsonSerializer.Deserialize<CallToolResult>(completed.Result, McpJsonUtilities.DefaultOptions)!;
        result.Content[0].ShouldBeOfType<TextContentBlock>().Text.ShouldContain("hello from host A", Case.Sensitive);
    }

    [Fact]
    public async Task Second_instance_reconstructs_an_approval_rejection_generically_not_with_todays_exact_wording()
    {
        // The counterpart to McpTasksEndpointTests's cache-hit K-103 assertion:
        // this is the fallback path (§ McpTaskStatusMapping), which cannot
        // recover the exact tool name from IRunStore alone -- a documented,
        // deliberate difference from the same-instance wording, not a bug.
        const string ApprovalModel = "cross-instance-approval-model";
        using var database = new TempSqliteDatabase("mcp-task-cross-instance");

        await using var hostA = await TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder
                    .UseSqlite(database.ConnectionString)
                    .AddTool(
                        (Func<string, string>)(orderId => $"canceled: {orderId}"),
                        name: "cancel_order",
                        description: "Cancels an order.",
                        configure: options => options.RequiresApproval = true)
                    .AddModelProvider(new Tracon.Testing.FakeModelProvider("routing")
                        .ForModel(ApprovalModel, cfg => cfg.CallsTool("cancel_order", new { orderId = "123" })))
                    .UseMcpServer(o =>
                    {
                        o.ExposedAgents.Add("db-agent");
                        o.EnableTasks = true;
                    });
            },
            configureAfterMap: app => app.MapTraconMcpServer());

        var createResponse = await hostA.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents", UriKind.Relative),
            TestData.Request() with { Model = new ModelBinding { Provider = "routing", Model = ApprovalModel } });
        createResponse.EnsureSuccessStatusCode();

        var updateResponse = await hostA.Client.PutAsJsonAsync(
            new Uri("/tracon/api/agents/db-agent", UriKind.Relative),
            TestData.Request() with
            {
                Model = new ModelBinding { Provider = "routing", Model = ApprovalModel },
                ToolNames = ["cancel_order"],
            });
        updateResponse.EnsureSuccessStatusCode();

        await using var clientA = await McpTaskTestClient.ConnectAsync(hostA.Client, "/tracon/mcp");

        var outcome = await clientA.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "tracon_db-agent", Arguments = Arguments("cancel my order") });

        var taskId = outcome.TaskCreated!.TaskId;

        await Poll(() => clientA.GetTaskAsync(taskId), r => r is CompletedTaskResult);

        // Host B never registers the approval-requiring tool or the
        // agent at all -- it only needs to READ the run the shared
        // database already has. Its own catalog is irrelevant to
        // tasks/get, which never re-resolves the agent.
        await using var hostB = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .UseSqlite(database.ConnectionString)
                .UseMcpServer(o =>
                {
                    o.ExposedAgents.Add("db-agent");
                    o.EnableTasks = true;
                }),
            configureAfterMap: app => app.MapTraconMcpServer());

        await using var clientB = await McpTaskTestClient.ConnectAsync(hostB.Client, "/tracon/mcp");

        var final = await Poll(() => clientB.GetTaskAsync(taskId), r => r is CompletedTaskResult);

        final.ShouldNotBeOfType<InputRequiredTaskResult>();

        var completed = final.ShouldBeOfType<CompletedTaskResult>();
        var result = JsonSerializer.Deserialize<CallToolResult>(completed.Result, McpJsonUtilities.DefaultOptions)!;
        var text = result.Content[0].ShouldBeOfType<TextContentBlock>().Text;

        text.ShouldContain("requires user approval", Case.Sensitive);
        // The generic fallback wording, not the cache-hit wording: no
        // tool name, because IRunStore alone cannot recover it.
        text.ShouldNotContain("'cancel_order'", Case.Sensitive);
    }

    private static Task<TraconTestHost> StartAsync(string connectionString)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .UseSqlite(connectionString)
                .AddAgent(TestData.Definition())
                .UseMcpServer(o =>
                {
                    o.ExposedAgents.Add("kod-agent");
                    o.EnableTasks = true;
                }),
            configureAfterMap: app => app.MapTraconMcpServer());

    private static Dictionary<string, JsonElement> Arguments(string message)
        => new(StringComparer.Ordinal) { ["message"] = JsonSerializer.SerializeToElement(message) };

    private static Task<T> Poll<T>(Func<ValueTask<T>> read, Func<T, bool> isDone)
        => WaitUntil.ValueAsync(async () => await read(), isDone, "the task to reach the expected state");
}
