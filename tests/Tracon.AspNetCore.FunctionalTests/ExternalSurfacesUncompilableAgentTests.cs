using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// What the MCP server and the A2A endpoint answer when the agent they were asked for cannot be compiled: the protocol's own
/// error, never a server failure, and no word about the configuration that is missing.
/// </summary>
/// <remarks>
/// A tenant provider binding whose configuration key has no value makes the catalog refuse the agent. The agent card and the
/// tool list do not compile an agent and answer as they always do; a call to the agent answers a protocol-level error with
/// a fixed text, so the name of the key a tenant's operator mis-typed does not reach a remote agent or tool caller.
/// </remarks>
public sealed class ExternalSurfacesUncompilableAgentTests
{
    private const string Doomed = "doomed-agent";

    private const string Healthy = "healthy-agent";

    private const string MissingKey = "Tracon:ProviderKeys:missing-on-purpose";

    [Fact]
    public async Task A2A_answers_a_protocol_error_that_names_no_configuration_and_the_card_and_the_healthy_agent_are_unaffected()
    {
        await using var host = await StartAsync();

        using var card = await host.Client.GetAsync(new Uri($"/tracon/a2a/{Doomed}/.well-known/agent-card.json", UriKind.Relative));
        card.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var doomed = await SendAsync(host, Doomed);
        doomed.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await doomed.Content.ReadAsStringAsync();
        body.ShouldContain("\"error\"");
        body.ShouldNotContain(MissingKey);
        body.ShouldNotContain("provider binding");

        using var healthy = await SendAsync(host, Healthy);
        (await healthy.Content.ReadAsStringAsync()).ShouldContain("\"result\"");
    }

    [Fact]
    public async Task The_MCP_server_lists_both_agents_and_answers_a_call_to_the_broken_one_with_a_tool_error_that_names_no_configuration()
    {
        await using var host = await StartAsync();

        await using var mcp = await McpTaskTestClient.ConnectAsync(host.Client, "/tracon/mcp");

        var tools = await mcp.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        tools.Select(static tool => tool.Name).Order(StringComparer.Ordinal).ShouldBe([$"tracon_{Doomed}", $"tracon_{Healthy}"]);

        var doomed = await CallAsync(mcp, $"tracon_{Doomed}");
        doomed.IsError.ShouldBe(true);
        TextOf(doomed).ShouldNotContain(MissingKey);

        var healthy = await CallAsync(mcp, $"tracon_{Healthy}");
        healthy.IsError.ShouldNotBe(true);
        TextOf(healthy).ShouldContain("Echo: hi");
    }

    private static async Task<TraconTestHost> StartAsync()
    {
        var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddModelProvider(new FakeModelProvider("doomed-provider").ForModel("m", cfg => cfg.EchoesUserMessage()))
                .AddModelProvider(new FakeModelProvider("healthy-provider").ForModel("m", cfg => cfg.EchoesUserMessage()))
                .AddAgent(TestData.Definition(Doomed) with { Model = new ModelBinding { Provider = "doomed-provider", Model = "m" } })
                .AddAgent(TestData.Definition(Healthy) with { Model = new ModelBinding { Provider = "healthy-provider", Model = "m" } })
                .UseA2A(static options => { options.ExposedAgents.Add(Doomed); options.ExposedAgents.Add(Healthy); })
                .UseMcpServer(static options => { options.ExposedAgents.Add(Doomed); options.ExposedAgents.Add(Healthy); }),
            configureAfterMap: static app =>
            {
                app.MapTraconA2A();
                app.MapTraconMcpServer();
            });

        await host.Services.GetRequiredService<ITenantProviderBindingStore>().UpsertAsync(
            new TenantProviderBinding
            {
                TenantId = host.Services.GetRequiredService<ITenantContext>().TenantId,
                ProviderName = "doomed-provider",
                ApiKeyConfigurationName = MissingKey,
                UpdatedAt = DateTimeOffset.UtcNow,
            });

        return host;
    }

    private static Task<HttpResponseMessage> SendAsync(TraconTestHost host, string agent)
        => host.Client.PostAsJsonAsync(
            new Uri($"/tracon/a2a/{agent}", UriKind.Relative),
            new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "SendMessage",
                @params = new { message = new { role = "ROLE_USER", parts = new[] { new { text = "hi" } }, messageId = Guid.NewGuid().ToString() } },
            });

    private static ValueTask<CallToolResult> CallAsync(McpClient mcp, string tool)
        => mcp.CallToolAsync(
            tool,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["message"] = "hi" },
            cancellationToken: TestContext.Current.CancellationToken);

    private static string TextOf(CallToolResult result)
        => string.Join(" | ", result.Content.OfType<TextContentBlock>().Select(static block => block.Text));
}
