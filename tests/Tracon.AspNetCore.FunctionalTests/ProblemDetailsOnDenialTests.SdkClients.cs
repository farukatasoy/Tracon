using System.Net;
using A2A;
using ModelContextProtocol.Client;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The refusal as the official MCP and A2A client libraries see it, next to the OpenAI SDK case in the main part of the
/// class.
/// </summary>
/// <remarks>
/// <para>
/// The differential tests of the two libraries run on hosts that do not ask for the problem body, so what the clients make
/// of a <c>401</c> or <c>403</c> that carries one, with a <c>WWW-Authenticate</c> challenge beside it, was never read. A
/// client library that took the body for a protocol message would lose the status, or one that took the challenge for an
/// OAuth discovery document would start a flow that nobody asked for.
/// </para>
/// </remarks>
public sealed partial class ProblemDetailsOnDenialTests
{
    private const string AgentName = "probe";

    [Fact]
    public async Task The_mcp_sdk_raises_a_refused_role_with_its_status_and_the_server_message()
    {
        await using var host = await StartSdkHostAsync();

        var refused = await Should.ThrowAsync<Exception>(async () => await ConnectMcpAsync(host, "some-other-role"));

        StatusOf(refused).ShouldBe(HttpStatusCode.Forbidden, refused.ToString());
        refused.ToString().ShouldContain(ProblemDetailsOnDenialHandler.ForbiddenDetail, Case.Sensitive);
    }

    [Fact]
    public async Task The_mcp_sdk_raises_a_caller_with_no_identity_with_its_status_and_the_server_message()
    {
        await using var host = await StartSdkHostAsync();

        // The challenge names no authorization server, so the library has nothing to discover: it must give up with the
        // refusal and not wait for a flow nobody started.
        var refused = await Should.ThrowAsync<Exception>(
            async () => await ConnectMcpAsync(host, role: null).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken));

        refused.ShouldNotBeOfType<TimeoutException>("the library waited instead of raising the refusal");
        StatusOf(refused).ShouldBe(HttpStatusCode.Unauthorized, refused.ToString());
        refused.ToString().ShouldContain(ProblemDetailsOnDenialHandler.UnauthorizedDetail, Case.Sensitive);
    }

    [Fact]
    public async Task The_mcp_sdk_connects_when_the_role_is_accepted()
    {
        await using var host = await StartSdkHostAsync();

        await using var client = await ConnectMcpAsync(host, AcceptedRole);

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        tools.Any(static tool => string.Equals(tool.Name, $"tracon_{AgentName}", StringComparison.Ordinal)).ShouldBeTrue();
    }

    [Fact]
    public async Task The_a2a_sdk_raises_a_refused_role_on_the_card_and_on_a_message()
    {
        await using var host = await StartSdkHostAsync();
        host.Client.DefaultRequestHeaders.Add(RoleHeader, "some-other-role");

        var card = await Should.ThrowAsync<Exception>(
            async () => await new A2ACardResolver(new Uri(host.Client.BaseAddress!, $"/tracon/a2a/{AgentName}/"), host.Client)
                .GetAgentCardAsync(TestContext.Current.CancellationToken));
        var message = await Should.ThrowAsync<Exception>(
            async () => await new A2AClient(new Uri(host.Client.BaseAddress!, $"/tracon/a2a/{AgentName}"), host.Client)
                .SendMessageAsync(
                    new SendMessageRequest
                    {
                        Message = new Message { Role = Role.User, MessageId = Guid.NewGuid().ToString("N"), Parts = [Part.FromText("hi")] },
                    },
                    TestContext.Current.CancellationToken));

        // Measured: the library keeps the status and drops the body (the card raises an A2AException around an
        // HttpRequestException whose message is only the status line, the message call the HttpRequestException itself), so
        // a body that is not a JSON-RPC message does not cost the caller its 403. The text of the body is for the clients
        // that read it, the OpenAI SDK among them.
        foreach (var (what, failure) in new[] { ("card", card), ("message", message) })
        {
            StatusOf(failure).ShouldBe(HttpStatusCode.Forbidden, $"{what}: {failure}");
        }
    }

    private static Task<TraconTestHost> StartSdkHostAsync()
        => StartAsync(
            optIn: true,
            configureTracon: static builder => builder
                .AddAgent(TestData.Definition(AgentName))
                .UseMcpServer(static options => options.ExposedAgents.Add(AgentName))
                .UseA2A(static options => options.ExposedAgents.Add(AgentName)),
            configureEndpoints: static options => options.RequireAuthorization(TraconPolicies.Reader),
            configureAfterMap: static app =>
            {
                app.MapTraconMcpServer();
                app.MapTraconA2A();
            });

    private static Task<McpClient> ConnectMcpAsync(TraconTestHost host, string? role)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);

        if (role is not null)
        {
            headers[RoleHeader] = role;
        }

        return McpClient.CreateAsync(
            new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Endpoint = new Uri(host.Client.BaseAddress!, "/tracon/mcp"),
                    AdditionalHeaders = headers,
                },
                host.Client,
                ownsHttpClient: false),
            new McpClientOptions { DiscoverProbeTimeout = TimeSpan.FromSeconds(30) },
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private static HttpStatusCode? StatusOf(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException { StatusCode: { } status })
            {
                return status;
            }
        }

        return null;
    }
}
