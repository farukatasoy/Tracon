using System.Net;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Connects the official MCP C# client SDK to the MCP server Tracon exposes and walks the protocol the way an MCP host
/// does: initialize, list, call, refuse, reconnect.
/// </summary>
/// <remarks>
/// The raw JSON-RPC tests prove the server answers what its author asked. The SDK client negotiates the protocol
/// version itself, validates the tool schemas it receives and decides what to do with a refusal, so a mismatch shows up
/// here as an exception from the SDK and nowhere else. The server publishes tools only; resources and prompts are
/// checked as absent, not as empty.
/// </remarks>
public sealed class McpSdkDifferentialTests
{
    private const string Token = "mcp-sdk-token";
    private const string McpPath = "/tracon/mcp";

    [Fact]
    public async Task The_SDK_initializes_and_reads_the_server_identity_and_advertises_tools_only()
    {
        await using var host = await StartAsync();
        await using var client = await ConnectAsync(host);

        client.ServerInfo.ShouldNotBeNull();
        client.ServerInfo.Name.ShouldNotBeNullOrWhiteSpace();
        client.NegotiatedProtocolVersion.ShouldNotBeNullOrWhiteSpace();
        client.ServerCapabilities.Tools.ShouldNotBeNull("the server publishes the exposed agents as tools");
        client.ServerCapabilities.Resources.ShouldBeNull("the server publishes no resources");
        client.ServerCapabilities.Prompts.ShouldBeNull("the server publishes no prompts");
    }

    [Fact]
    public async Task The_SDK_lists_an_exposed_agent_as_a_tool_with_a_valid_object_input_schema()
    {
        await using var host = await StartAsync();
        await using var client = await ConnectAsync(host);

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var tool = tools.ShouldHaveSingleItem();
        tool.Name.ShouldBe("tracon_kod-agent");
        tool.Description.ShouldNotBeNullOrWhiteSpace();
        tool.JsonSchema.GetProperty("type").GetString().ShouldBe("object");
        tool.JsonSchema.GetProperty("properties").GetProperty("message").GetProperty("type").GetString().ShouldBe("string");
    }

    [Fact]
    public async Task The_SDK_calls_the_agent_tool_and_reads_the_text_result()
    {
        await using var host = await StartAsync();
        await using var client = await ConnectAsync(host);

        var result = await client.CallToolAsync(
            "tracon_kod-agent",
            Args("hello sdk"),
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldNotBe(true);
        result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem().Text.ShouldContain("hello sdk", Case.Sensitive);
    }

    [Fact]
    public async Task The_SDK_sees_an_unknown_tool_and_a_call_without_the_required_message_as_errors_not_as_success()
    {
        await using var host = await StartAsync();
        await using var client = await ConnectAsync(host);

        var unknown = await CallOrErrorAsync(client, "no_such_tool", Args("x"));
        unknown.ShouldBeTrue("an unknown tool is an error the SDK surfaces");

        var missing = await CallOrErrorAsync(client, "tracon_kod-agent", new Dictionary<string, object?>(StringComparer.Ordinal));
        missing.ShouldBeTrue("a call without the required argument is an error the SDK surfaces");
    }

    [Fact]
    public async Task The_SDK_gets_a_not_found_answer_with_the_MethodNotFound_error_for_resources_and_prompts()
    {
        await using var host = await StartAsync();
        await using var client = await ConnectAsync(host);

        // The server sends the JSON-RPC MethodNotFound error (-32601). On the modern protocol the SDK's own server
        // pairs it with HTTP 404 and its client raises HttpRequestException, whose message carries the error body;
        // on the previous protocol the same error arrives as an McpException. Either way the call fails fast and
        // names the method, which is what a host needs to stop asking.
        foreach (var call in new Func<Task>[]
        {
            async () => await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken),
            async () => await client.ListPromptsAsync(cancellationToken: TestContext.Current.CancellationToken),
        })
        {
            var failure = await Should.ThrowAsync<Exception>(call);

            (failure is McpException || failure is HttpRequestException).ShouldBeTrue(failure.ToString());
            failure.ToString().ShouldContain("-32601", Case.Sensitive);
        }
    }

    [Fact]
    public async Task A_client_pinned_to_the_previous_protocol_version_negotiates_it_and_calls_the_agent()
    {
        await using var host = await StartAsync();
        await using var client = await McpClient.CreateAsync(
            Transport(host, token: null),
            new McpClientOptions { ProtocolVersion = "2025-11-25" },
            cancellationToken: TestContext.Current.CancellationToken);

        client.NegotiatedProtocolVersion.ShouldBe("2025-11-25");

        var result = await client.CallToolAsync("tracon_kod-agent", Args("legacy handshake"), cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldNotBe(true);
        result.Content.OfType<TextContentBlock>().Single().Text.ShouldContain("legacy handshake", Case.Sensitive);
    }

    [Fact]
    public async Task A_missing_or_wrong_bearer_token_is_a_401_the_SDK_raises_at_connect_time()
    {
        await using var host = await StartAsync(authToken: Token);

        var missing = await Should.ThrowAsync<Exception>(async () => await ConnectAsync(host, token: null));
        var wrong = await Should.ThrowAsync<Exception>(async () => await ConnectAsync(host, token: "not-the-token"));

        StatusOf(missing).ShouldBe(HttpStatusCode.Unauthorized, missing.ToString());
        StatusOf(wrong).ShouldBe(HttpStatusCode.Unauthorized, wrong.ToString());

        await using var accepted = await ConnectAsync(host, token: Token);
        (await accepted.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_client_that_closes_and_connects_again_starts_a_fresh_handshake_because_the_server_keeps_no_session()
    {
        await using var host = await StartAsync();

        var first = await ConnectAsync(host);
        first.SessionId.ShouldBeNull("the server is stateless: it issues no Mcp-Session-Id, so there is nothing to lose and nothing to resume");
        (await first.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken)).ShouldHaveSingleItem();
        await first.DisposeAsync();

        await using var second = await ConnectAsync(host);
        (await second.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken)).ShouldHaveSingleItem();

        var result = await second.CallToolAsync("tracon_kod-agent", Args("after reconnect"), cancellationToken: TestContext.Current.CancellationToken);
        result.Content.OfType<TextContentBlock>().Single().Text.ShouldContain("after reconnect", Case.Sensitive);
    }

    [Fact]
    public async Task Two_concurrent_SDK_clients_both_run_the_agent_and_each_reads_its_own_answer()
    {
        await using var host = await StartAsync();
        await using var one = await ConnectAsync(host);
        await using var two = await ConnectAsync(host);

        var calls = await Task.WhenAll(
            one.CallToolAsync("tracon_kod-agent", Args("from one"), cancellationToken: TestContext.Current.CancellationToken).AsTask(),
            two.CallToolAsync("tracon_kod-agent", Args("from two"), cancellationToken: TestContext.Current.CancellationToken).AsTask());

        calls[0].Content.OfType<TextContentBlock>().Single().Text.ShouldContain("from one", Case.Sensitive);
        calls[1].Content.OfType<TextContentBlock>().Single().Text.ShouldContain("from two", Case.Sensitive);
    }

    private static Dictionary<string, object?> Args(string message)
        => new(StringComparer.Ordinal) { ["message"] = message };

    private static async Task<bool> CallOrErrorAsync(McpClient client, string tool, Dictionary<string, object?> arguments)
    {
        try
        {
            var result = await client.CallToolAsync(tool, arguments, cancellationToken: TestContext.Current.CancellationToken);

            return result.IsError == true;
        }
        catch (McpException)
        {
            return true;
        }
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

    private static Task<TraconTestHost> StartAsync(string? authToken = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(o => o.ExposedAgents.Add("kod-agent")),
            configureEndpoints: options => options.AuthToken = authToken,
            configureAfterMap: app => app.MapTraconMcpServer());

    private static Task<McpClient> ConnectAsync(TraconTestHost host, string? token = null)
        => McpClient.CreateAsync(
            Transport(host, token),
            new McpClientOptions { DiscoverProbeTimeout = TimeSpan.FromSeconds(30) },
            cancellationToken: TestContext.Current.CancellationToken);

    private static HttpClientTransport Transport(TraconTestHost host, string? token)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);

        if (token is not null)
        {
            headers["Authorization"] = $"Bearer {token}";
        }

        return new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(host.Client.BaseAddress!, McpPath),
                AdditionalHeaders = headers,
            },
            host.Client,
            ownsHttpClient: false);
    }
}
