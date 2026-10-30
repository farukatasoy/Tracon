using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>
/// Behaviour 4: MCP discovery runs on every node, also while single-executor election is on.
/// </summary>
/// <remarks>
/// <para>
/// Until discovery ran under a lease: with <c>Tracon:SingletonExecution:Enabled</c> on, only the node that
/// held it refreshed its tool catalog. The catalog is a cache of the process and holds the live connection a tool call
/// goes through, so the other node had no MCP tool at all.
/// </para>
/// <para>
/// The election itself is the SQL lease table of the provider under test, so this scenario also proves that two nodes
/// share one lease row without either losing its catalog.
/// </para>
/// <para>
/// The server is saved straight into the shared store, not through the HTTP endpoint: the endpoint refreshes the
/// catalog of the node that receives it, and that node would list the tool whichever node holds the lease. Only the
/// discovery loops are left to fill the two catalogs.
/// </para>
/// </remarks>
public abstract partial class TwoNodeAcceptanceContract
{
    [Fact]
    public async Task Both_nodes_list_the_tool_while_single_executor_election_is_on()
    {
        await using var server = await LookupMcpServer.StartAsync("from-the-server");
        await using var pair = await TwoNodeCluster.StartAsync(
            NewDatabase(),
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

        await WaitUntil.ValueAsync(() => OAuthFlowClient.ToolNamesAsync(pair.A), OAuthFlowClient.HasLookup, "node A to list the MCP tool");
        await WaitUntil.ValueAsync(() => OAuthFlowClient.ToolNamesAsync(pair.B), OAuthFlowClient.HasLookup, "node B to list the MCP tool");
    }
}
