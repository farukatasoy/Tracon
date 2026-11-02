using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// A response over <see cref="TraconMcpOptions.MaxResponseBytes"/> fails the call that asked for it, through the connection the
/// catalog opened, and the next call on the same server is answered.
/// </summary>
/// <remarks>
/// The handler is checked on its own in <c>McpResponseLimitHandlerTests</c>. Here the option travels the whole way: from
/// <c>TraconMcpOptions</c> through the connection and the transport factory to a real HTTP response. A 20 MB tool result used to
/// cost about 500 MB of memory before the output limit could trim it.
/// </remarks>
public sealed class McpResponseLimitTransportTests
{
    private const int Limit = 64 * 1024;

    private const string Tool = """{"name":"lookup","description":"x","inputSchema":{"type":"object","properties":{}}}""";

    [Fact]
    public async Task A_result_over_the_limit_fails_its_call_and_the_next_call_is_answered()
    {
        var big = false;

        await using var server = ScriptedMcpServer.Start((method, _, _, _) => Task.FromResult<string?>(method switch
        {
            "tools/list" => "{\"tools\":[" + Tool + "]}",
            "tools/call" => "{\"content\":[{\"type\":\"text\",\"text\":\"" + (Volatile.Read(ref big) ? new string('x', Limit * 3) : "small") + "\"}]}",
            _ => "{}",
        }));

        var servers = new InMemoryMcpServerStore();
        await servers.SaveAsync(
            new McpServerDefinition { Id = Guid.NewGuid(), TenantId = "default", Name = "srv", Endpoint = server.Endpoint },
            TestContext.Current.CancellationToken);

        await using var catalog = new McpToolCatalog(
            servers,
            new InMemoryTenantStore(),
            new EmptyMcpConfiguration(),
            Options.Create(new TraconOptions()),
            Options.Create(new TraconMcpOptions { MaxResponseBytes = Limit }),
            NullLoggerFactory.Instance,
            new McpOAuthTokenCacheRegistry(),
            new AllowAllToolAuthorizationHandler(),
            McpToolPolicyTestGates.Open(),
            NoOpToolArgumentsValidator.Instance,
            attribution: null,
            egressGuard: new EgressSocketGuard(static () => new EgressAddressPolicy(AllowPrivateNetworkTargets: true, AllowLoopback: true)));

        await catalog.RefreshAsync(TestContext.Current.CancellationToken);
        catalog.ForTenant("default").TryGet("srv_lookup", out var tool).ShouldBeTrue();

        // The size of the answer is the server's choice, and a small one passes.
        (await Call(tool!)).ShouldContain("small");

        Volatile.Write(ref big, true);

        var failure = await Should.ThrowAsync<Exception>(() => Call(tool!));
        Chain(failure).ShouldContain("Tracon:Mcp:MaxResponseBytes");

        Volatile.Write(ref big, false);

        (await Call(tool!)).ShouldContain("small");
    }

    private static async Task<string> Call(AIFunctionDeclaration tool)
        => (await ((AIFunction)tool).InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal), TestContext.Current.CancellationToken))?.ToString() ?? string.Empty;

    private static string Chain(Exception exception)
    {
        var messages = new List<string>();

        for (var inner = exception; inner is not null; inner = inner.InnerException)
        {
            messages.Add(inner.Message);
        }

        return string.Join(" | ", messages);
    }
}
