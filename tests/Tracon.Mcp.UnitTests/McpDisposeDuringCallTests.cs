using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// A catalog or a connection disposed while a call holds its gate does not fail that call.
/// </summary>
/// <remarks>
/// A host that stops disposes the catalog, and a refresh that replaces a connection disposes the old one, while a run may
/// still be inside either. The call owns the gate until its <c>finally</c>; a gate the dispose has already closed turned
/// that <c>finally</c> into an <see cref="ObjectDisposedException"/> and took the call's own result with it.
/// </remarks>
public sealed class McpDisposeDuringCallTests
{
    private const string Capabilities = "{\"tools\":{},\"resources\":{}}";

    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task A_refresh_in_flight_when_the_catalog_is_disposed_still_returns_its_outcome()
    {
        var listingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishListing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var server = ScriptedMcpServer.Start(
            async (method, _, _, cancellationToken) =>
            {
                if (!string.Equals(method, "tools/list", StringComparison.Ordinal))
                {
                    return "{}";
                }

                listingStarted.TrySetResult();
                await finishListing.Task.WaitAsync(cancellationToken);

                return "{\"tools\":[]}";
            },
            Capabilities);

        var catalog = NewCatalog(await StoreAsync(server));

        var refresh = catalog.RefreshAsync(TestContext.Current.CancellationToken).AsTask();

        await listingStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        await catalog.DisposeAsync();
        finishListing.SetResult();

        var outcome = await refresh;

        outcome.ToolCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_connection_made_after_the_catalog_was_disposed_is_closed_and_not_kept()
    {
        var listingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishListing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var server = ScriptedMcpServer.Start(
            async (method, _, _, cancellationToken) =>
            {
                if (!string.Equals(method, "tools/list", StringComparison.Ordinal))
                {
                    return "{}";
                }

                listingStarted.TrySetResult();
                await finishListing.Task.WaitAsync(cancellationToken);

                return "{\"tools\":[]}";
            },
            Capabilities);

        var catalog = NewCatalog(await StoreAsync(server));

        var refresh = catalog.RefreshAsync(TestContext.Current.CancellationToken).AsTask();

        // The refresh is inside its first connection when the catalog is disposed: the dispose walks an empty
        // dictionary, and the connection that finishes afterwards used to be written into it and left open.
        await listingStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await catalog.DisposeAsync();
        finishListing.SetResult();
        await refresh;

        catalog.TryGetConnection("default", "srv", out _).ShouldBeFalse("a disposed catalog keeps no connection");
    }

    [Fact]
    public async Task A_resource_read_in_flight_when_the_connection_is_disposed_returns_a_status()
    {
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var server = ScriptedMcpServer.Start(
            async (method, _, _, cancellationToken) =>
            {
                switch (method)
                {
                    case "resources/list":
                        return "{\"resources\":[{\"uri\":\"res://1\",\"name\":\"resource 1\"}]}";
                    case "resources/read":
                        readStarted.TrySetResult();
                        await finishRead.Task.WaitAsync(cancellationToken);

                        return "{\"contents\":[{\"uri\":\"res://1\",\"mimeType\":\"text/plain\",\"text\":\"hello\"}]}";
                    default:
                        return "{}";
                }
            },
            Capabilities);

        await using var catalog = NewCatalog(await StoreAsync(server));

        await catalog.RefreshAsync(TestContext.Current.CancellationToken);

        catalog.TryGetConnection("default", "srv", out var connection).ShouldBeTrue("the server was not reached");

        var options = new TraconMcpOptions { ConnectionTimeout = ConnectionTimeout };
        var read = connection!
            .ReadDeclaredResourceAsync("res://1", 1024, options, NullLogger.Instance, TestContext.Current.CancellationToken)
            .AsTask();

        await readStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        await connection.DisposeAsync();
        finishRead.SetResult();

        var (status, _) = await read;

        Enum.IsDefined(status).ShouldBeTrue();
    }

    [Fact]
    public async Task The_read_resource_tool_of_a_closed_connection_says_the_connection_is_closed()
    {
        await using var server = ScriptedMcpServer.Start(
            static (method, _, _, _) => Task.FromResult<string?>(method switch
            {
                "tools/list" => "{\"tools\":[]}",
                "resources/list" => "{\"resources\":[{\"uri\":\"res://1\",\"name\":\"one\"}]}",
                _ => "{}",
            }),
            Capabilities);

        var catalog = NewCatalog(await StoreAsync(server));

        await catalog.RefreshAsync(TestContext.Current.CancellationToken);

        var tools = catalog.ForTenant("default");
        var name = tools.Descriptors.Select(static tool => tool.Name).Single(static name => name.EndsWith("read_resource", StringComparison.Ordinal));

        tools.TryGet(name, out var declared).ShouldBeTrue();

        // A refresh that replaced the connection, or a host that stopped, closed it; a run still holds the tool.
        await catalog.DisposeAsync();

        var failure = await Should.ThrowAsync<Exception>(async () => await ((AIFunction)declared!).InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?>(StringComparer.Ordinal) { ["uri"] = "res://1" }, StringComparer.Ordinal),
            TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("closed", Case.Sensitive);
        failure.Message.ShouldNotContain("not among the resources declared", Case.Sensitive);
    }

    private static McpToolCatalog NewCatalog(InMemoryMcpServerStore servers)
        => new(
            servers,
            new InMemoryTenantStore(),
            new EmptyMcpConfiguration(),
            Options.Create(new TraconOptions()),
            Options.Create(new TraconMcpOptions { ConnectionTimeout = ConnectionTimeout }),
            NullLoggerFactory.Instance,
            new McpOAuthTokenCacheRegistry(),
            new AllowAllToolAuthorizationHandler(),
            McpToolPolicyTestGates.Open(),
            NoOpToolArgumentsValidator.Instance,
            attribution: null,
            egressGuard: new EgressSocketGuard(static () => new EgressAddressPolicy(AllowPrivateNetworkTargets: true, AllowLoopback: true)));

    private static async Task<InMemoryMcpServerStore> StoreAsync(ScriptedMcpServer server)
    {
        var servers = new InMemoryMcpServerStore();

        await servers.SaveAsync(
            new McpServerDefinition { Id = Guid.NewGuid(), TenantId = "default", Name = "srv", Endpoint = server.Endpoint },
            TestContext.Current.CancellationToken);

        return servers;
    }
}
