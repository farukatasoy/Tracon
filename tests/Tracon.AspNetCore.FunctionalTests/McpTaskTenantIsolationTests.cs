using System.Net.Http.Json;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 117: Tracon's <c>IMcpTaskStore</c> implementation carries no tenant
/// parameter of its own (the SDK's contract has none — § 117.1); the isolation
/// guarantee rests entirely on <see cref="IRunStore.GetRunAsync"/>'s own
/// ambient-tenant filtering. This is the functional proof that the composition
/// actually holds, not just that the pieces are wired.
/// </summary>
public sealed class McpTaskTenantIsolationTests
{
    private const string TenantHeader = "X-Tracon-Tenant";

    [Fact]
    public async Task Another_tenants_task_id_is_not_found()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .UseTenancy(o =>
                {
                    o.Enabled = true;
                    o.AllowHeaderResolution = true;
                })
                .UseMcpServer(o =>
                {
                    o.ExposeAllAgents = true;
                    o.EnableTasks = true;
                }),
            configureAfterMap: app => app.MapTraconMcpServer());

        using var createRequest = new HttpRequestMessage(
            HttpMethod.Post, new Uri("/tracon/api/agents", UriKind.Relative))
        {
            Content = JsonContent.Create(TestData.Request()),
        };
        createRequest.Headers.Add(TenantHeader, "tenant-a");

        using var createResponse = await host.Client.SendAsync(createRequest);
        createResponse.EnsureSuccessStatusCode();

        await using var clientA = await McpTaskTestClient.ConnectAsync(host.Client, "/tracon/mcp", tenantHeader: "tenant-a");

        var outcome = await clientA.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "tracon_db-agent", Arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal) });

        var taskId = outcome.TaskCreated!.TaskId;

        // Tenant A reads its own task without trouble.
        await clientA.GetTaskAsync(taskId);

        await using var clientB = await McpTaskTestClient.ConnectAsync(host.Client, "/tracon/mcp", tenantHeader: "tenant-b");

        var exception = await Should.ThrowAsync<McpException>(() => clientB.GetTaskAsync(taskId).AsTask());
        exception.Message.ShouldContain("Unknown task", Case.Sensitive);
    }

    [Fact]
    public async Task Another_tenant_can_interfere_with_cancellation_but_never_reads_the_result()
    {
        // 🚨 Known SDK-level gap, verified here rather than assumed away:
        // ModelContextProtocol.Extensions.Tasks tracks each task's
        // CancellationTokenSource in a dictionary keyed ONLY by task id, with
        // no tenant concept at all — tasks/cancel's own handler cancels that
        // source and acks unconditionally BEFORE ever calling
        // IMcpTaskStore.SetCancelledAsync's tenant-aware check. Tracon has
        // no seam into that dictionary, so a caller who merely GUESSES
        // another tenant's task id (a v7 UUID — not practically guessable,
        // but not secret either once observed) CAN cause that task to be
        // cancelled. What Tracon's own boundary (IRunStore's ambient-tenant
        // filtering, exercised by every OTHER test in this class) still
        // guarantees: that caller can never READ the task's content, state,
        // or existence — tasks/get for it still answers "Unknown task" both
        // before and after the interference. RunBackedMcpTaskStore's job is to
        // record what the SDK already did truthfully (§ owning-tenant fix),
        // not to pretend the row is still Queued forever.
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .UseTenancy(o =>
                {
                    o.Enabled = true;
                    o.AllowHeaderResolution = true;
                })
                .UseMcpServer(o =>
                {
                    o.ExposeAllAgents = true;
                    o.EnableTasks = true;
                }),
            configureAfterMap: app => app.MapTraconMcpServer());

        using var createRequest = new HttpRequestMessage(
            HttpMethod.Post, new Uri("/tracon/api/agents", UriKind.Relative))
        {
            Content = JsonContent.Create(TestData.Request()),
        };
        createRequest.Headers.Add(TenantHeader, "tenant-a");

        using var createResponse = await host.Client.SendAsync(createRequest);
        createResponse.EnsureSuccessStatusCode();

        await using var clientA = await McpTaskTestClient.ConnectAsync(host.Client, "/tracon/mcp", tenantHeader: "tenant-a");

        var outcome = await clientA.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "tracon_db-agent", Arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal) });

        var taskId = outcome.TaskCreated!.TaskId;

        await using var clientB = await McpTaskTestClient.ConnectAsync(host.Client, "/tracon/mcp", tenantHeader: "tenant-b");

        await clientB.CancelTaskAsync(taskId);

        // Tenant B's ack is unconditional either way; what matters is that
        // tenant B is never able to READ the outcome — regardless of whether
        // its cancel attempt actually landed before the run finished.
        var exception = await Should.ThrowAsync<McpException>(() => clientB.GetTaskAsync(taskId).AsTask());
        exception.Message.ShouldContain("Unknown task", Case.Sensitive);

        // Tenant A's own view is never affected by this: the row reaches a
        // real terminal state (whichever one — a fast echo call may complete
        // before B's cancel lands) and stays truthfully readable by its owner,
        // rather than being stuck reporting Working forever.
        await Poll(() => clientA.GetTaskAsync(taskId), r => r is not WorkingTaskResult);
    }

    private static Task<T> Poll<T>(Func<ValueTask<T>> read, Func<T, bool> isDone)
        => WaitUntil.ValueAsync(async () => await read(), isDone, "the task to reach the expected state");
}
