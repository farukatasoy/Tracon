using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// MCP discovery runs on every instance, also while single-executor selection is on.
/// </summary>
/// <remarks>
/// <para>
/// Until this change discovery ran under a lease: with <c>Tracon:SingletonExecution:Enabled</c> on, only
/// the instance that held it refreshed its tool catalog. The catalog is a cache of the process and holds
/// the live connection a tool call goes through, so the other instance had no MCP tool at all. Measured on
/// two hosts that shared one database: 1 tool on the leader, 0 on the other instance, with or without
/// OAuth on the server.
/// </para>
/// <para>
/// The server is saved straight into the shared store, not through the HTTP endpoint: the endpoint
/// refreshes the catalog of the host that receives it, and that host would list the tool whichever instance
/// the lease belongs to. Only the discovery loops are left to fill the two catalogs.
/// </para>
/// </remarks>
public sealed class McpDiscoveryEveryInstanceTests
{
    [Fact]
    public async Task Both_instances_list_the_tool_while_single_executor_selection_is_on()
    {
        await using var server = await LookupMcpServer.StartAsync("from-the-server");
        await using var pair = await TwoHostFixture.StartAsync(
            configure: static builder => builder.UseMcp(),
            configureServices: static services =>
            {
                services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true);
                services.Configure<TraconMcpOptions>(static options => options.RefreshInterval = TimeSpan.FromMilliseconds(200));
                services.Configure<SingletonExecutionOptions>(static options =>
                {
                    options.Enabled = true;
                    options.LeaseDuration = TimeSpan.FromMinutes(5);
                });
            });

        await pair.A.Services.GetRequiredService<IMcpServerStore>().SaveAsync(
            new McpServerDefinition
            {
                Id = Guid.NewGuid(),
                TenantId = "default",
                Name = "lookup-server",
                Endpoint = server.Endpoint,
                RequiresApproval = false,
            },
            CancellationToken.None);

        await WaitUntil.ValueAsync(() => OAuthFlowClient.ToolNamesAsync(pair.A), OAuthFlowClient.HasLookup, "instance A to list the MCP tool");
        await WaitUntil.ValueAsync(() => OAuthFlowClient.ToolNamesAsync(pair.B), OAuthFlowClient.HasLookup, "instance B to list the MCP tool");
    }
}
