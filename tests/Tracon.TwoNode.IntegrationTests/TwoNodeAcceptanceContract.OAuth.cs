using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>
/// Behaviour 3: an MCP server that uses OAuth works on two nodes (F-766). The token is kept in the deployment's
/// distributed cache, the provider's redirect may reach either node, and one node at a time refreshes a token.
/// </summary>
/// <remarks>
/// The cache is one object both hosts share, as one Redis is for two replicas; the server definitions and the refresh
/// lease live in the SQL database. A real MCP server that is its own authorization server stands behind them
/// (<see cref="OAuthProtectedMcpServer"/>). The polling intervals are shortened; the shipped values are measured by the
/// manual acceptance cases.
/// </remarks>
public abstract partial class TwoNodeAcceptanceContract
{
    private static readonly McpOAuthDistributedTiming FastTiming = new()
    {
        CallbackPoll = TimeSpan.FromMilliseconds(50),
        OutcomePoll = TimeSpan.FromMilliseconds(50),
        RefreshPoll = TimeSpan.FromMilliseconds(50),
    };

    [Fact]
    public async Task A_redirect_that_reaches_the_other_node_completes_the_flow_and_both_nodes_list_the_tool()
    {
        await using var server = await OAuthProtectedMcpServer.StartAsync();
        await using var pair = await StartOAuthPairAsync(new SharedCache());

        await OAuthFlowClient.SaveServerAsync(pair.A, server.Endpoint);
        var state = await OAuthFlowClient.StartFlowAsync(pair.A);

        // The provider redirects to node B, which started nothing. Round-robin does this half of the time.
        (await OAuthFlowClient.CallbackAsync(pair.B, state)).ShouldContain("Authorization complete", Case.Sensitive);

        // The owner exchanged the code once, with the verifier only it holds.
        var exchange = server.TokenRequests.ShouldHaveSingleItem();
        exchange.GrantType.ShouldBe("authorization_code");
        exchange.HasCodeVerifier.ShouldBeTrue();

        await WaitUntil.ValueAsync(() => OAuthFlowClient.ToolNamesAsync(pair.A), OAuthFlowClient.HasLookup, "node A to list the MCP tool");
        await WaitUntil.ValueAsync(() => OAuthFlowClient.ToolNamesAsync(pair.B), OAuthFlowClient.HasLookup, "node B to list the MCP tool");
    }

    /// <summary>
    /// The call, not only the listing: an agent run on node B calls the tool of a server that node A authorized, and the
    /// server answers it.
    /// </summary>
    [Fact]
    public async Task A_tool_call_on_the_other_node_reaches_the_server_with_the_token()
    {
        var model = new Tracon.Testing.FakeModelProvider("oauth-model").EchoesLastToolResult();
        await using var server = await OAuthProtectedMcpServer.StartAsync();
        await using var pair = await StartOAuthPairAsync(new SharedCache(), model);

        await OAuthFlowClient.SaveServerAsync(pair.A, server.Endpoint);
        (await OAuthFlowClient.AuthorizeAsync(pair.A)).ShouldContain("Authorization complete", Case.Sensitive);
        await OAuthFlowClient.RefreshAsync(pair.B);
        await WaitUntil.ValueAsync(() => OAuthFlowClient.ToolNamesAsync(pair.B), OAuthFlowClient.HasLookup);

        using (var saved = await pair.B.Client.PostAsJsonAsync(
                   new Uri("/tracon/api/agents", UriKind.Relative),
                   new AgentDefinitionRequest
                   {
                       Name = "looker",
                       Instructions = "Look the value up.",
                       Model = new ModelBinding { Provider = "oauth-model", Model = "lookup" },
                       ToolNames = [$"{OAuthFlowClient.ServerName}_lookup"],
                   }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.Created, await saved.Content.ReadAsStringAsync());
        }

        model.CallsTool($"{OAuthFlowClient.ServerName}_lookup");

        using var run = await pair.B.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/looker/run", UriKind.Relative),
            new AgentRunRequest { Message = "look it up", SessionId = "oauth-b" });

        run.StatusCode.ShouldBe(HttpStatusCode.OK, await run.Content.ReadAsStringAsync());

        // The server answered "forty-two" because the request carried the token A obtained; B exchanged nothing.
        (await run.Content.ReadAsStringAsync()).ShouldContain("forty-two", Case.Sensitive);
        server.TokenRequests.ShouldHaveSingleItem();
    }

    /// <summary>
    /// 🚨 Two tenants save a server of the same name, at the same endpoint, with the same client id. Only one of them
    /// authorizes it. The other tenant must get no token from the cache the nodes share: the key holds the tenant.
    /// </summary>
    [Fact]
    public async Task A_token_belongs_to_the_tenant_that_authorized_it()
    {
        await using var server = await OAuthProtectedMcpServer.StartAsync();
        await using var pair = await StartOAuthPairAsync(new SharedCache(), tenancy: true);

        foreach (var slug in new[] { "tenant-a", "tenant-b" })
        {
            using var tenant = await pair.A.Client.PutAsJsonAsync(new Uri($"/tracon/api/tenants/{slug}", UriKind.Relative), new { displayName = slug });
            tenant.StatusCode.ShouldBe(HttpStatusCode.OK, await tenant.Content.ReadAsStringAsync());

            await OAuthFlowClient.SaveServerAsync(pair.A, server.Endpoint, slug);
        }

        var state = await OAuthFlowClient.StartFlowAsync(pair.A, "tenant-a");
        (await OAuthFlowClient.CallbackAsync(pair.B, state)).ShouldContain("Authorization complete", Case.Sensitive);

        await OAuthFlowClient.RefreshAsync(pair.B);

        // The refresh ran for both tenants: the first one's tool is there, so the snapshot that holds both is in place.
        await WaitUntil.ValueAsync(() => OAuthFlowClient.ToolNamesAsync(pair.B, "tenant-a"), OAuthFlowClient.HasLookup);
        OAuthFlowClient.HasLookup(await OAuthFlowClient.ToolNamesAsync(pair.B, "tenant-b")).ShouldBeFalse();
        server.TokenRequests.ShouldHaveSingleItem();
    }

    /// <summary>
    /// The refresh lease is a row in the SQL database both nodes share. The provider rotates the refresh token and
    /// refuses one it has seen, so a second refresh with the same token shows up as a rejection.
    /// </summary>
    [Fact]
    public async Task Concurrent_refreshes_on_two_nodes_call_the_token_endpoint_once()
    {
        await using var server = await OAuthProtectedMcpServer.StartAsync(refreshDelay: TimeSpan.FromMilliseconds(400));
        var cache = new SharedCache();
        await using var pair = await StartOAuthPairAsync(cache);

        await OAuthFlowClient.SaveServerAsync(pair.A, server.Endpoint);
        (await OAuthFlowClient.AuthorizeAsync(pair.A)).ShouldContain("Authorization complete", Case.Sensitive);
        await WaitUntil.ValueAsync(() => OAuthFlowClient.ToolNamesAsync(pair.A), OAuthFlowClient.HasLookup);

        // The token the cache holds has run out: the SDK now has to refresh it. Everything else about it is real.
        await ExpireStoredTokenAsync(pair.A);
        var refreshesBefore = server.RefreshGrants;

        await Task.WhenAll(OAuthFlowClient.RefreshAsync(pair.A), OAuthFlowClient.RefreshAsync(pair.B));

        await WaitUntil.ValueAsync(() => OAuthFlowClient.ToolNamesAsync(pair.B), OAuthFlowClient.HasLookup);
        server.RejectedRefreshes.ShouldBe(0, "a node that refreshed with a token already used would be refused.");
        (server.RefreshGrants - refreshesBefore).ShouldBe(1, "one node refreshed; the other read the new token from the cache.");
    }

    private static async Task ExpireStoredTokenAsync(TraconTestHost host)
    {
        var backend = host.Services.GetRequiredService<McpOAuthDistributedBackend>();
        var key = new McpTenantServerKey("default", OAuthFlowClient.ServerName);
        var tokens = new DistributedMcpTokenCache(
            backend,
            key,
            new McpTokenBinding(
                (await host.Services.GetRequiredService<IMcpServerStore>().GetAsync("default", OAuthFlowClient.ServerName))!.Endpoint.AbsoluteUri,
                OAuthFlowClient.ClientId),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        var token = (await tokens.GetTokensAsync(CancellationToken.None)).ShouldNotBeNull();
        token.ObtainedAt = DateTimeOffset.UtcNow.AddHours(-2);

        await tokens.StoreTokensAsync(token, CancellationToken.None);
    }

    private Task<TwoNodeCluster> StartOAuthPairAsync(
        SharedCache cache,
        Tracon.Testing.FakeModelProvider? model = null,
        bool tenancy = false)
    {
        var protector = McpOAuthKit.Protector();

        return TwoNodeCluster.StartAsync(
            NewDatabase(),
            configure: builder =>
            {
                builder.UseMcp();

                if (tenancy)
                {
                    builder.UseTenancy(static options =>
                    {
                        options.Enabled = true;
                        options.AllowHeaderResolution = true;
                    });
                }

                if (model is not null)
                {
                    builder.AddModelProvider(model);
                }
            },
            configureServices: services =>
            {
                services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true);
                services.Configure<TraconMcpOptions>(static options =>
                {
                    options.OAuthCallbackBaseUri = new Uri("http://localhost:5081/tracon/");
                    options.OAuthTokenStore = McpOAuthTokenStore.DistributedCache;
                });
                services.AddSingleton<IDistributedCache>(cache);
                services.AddSingleton(protector);
                services.AddSingleton(FastTiming);
            });
    }
}
