using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// The resource and prompt lists of a server are read page by page and stop at their limits, the way the tool list does.
/// </summary>
/// <remarks>
/// <para>
/// The SDK's collecting overloads follow the cursor until the server stops handing one out, so a server that never does filled
/// memory before any limit was looked at (3 GB in ten seconds for tools; the resource and prompt lists were read the same way).
/// The server here answers every page with one item and a cursor, and a limit of five keeps five items after six pages.
/// </para>
/// <para>
/// A resource can be read only if the server declares it. The declared set is the first <c>MaxResourcesPerServer</c>
/// resources, the same set the catalog keeps, so a URI past the limit is refused as not declared.
/// </para>
/// </remarks>
public sealed class McpListLimitTests
{
    private const int Limit = 5;

    private const string Capabilities = "{\"tools\":{},\"resources\":{},\"prompts\":{}}";

    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task A_resource_list_that_never_ends_is_cut_at_the_limit_after_one_page_past_it()
    {
        var pages = 0;
        await using var server = EndlessServer("resources/list", () => Interlocked.Increment(ref pages), static page => Resource(page));

        var result = await NewResourceClient(server).ListResourcesAsync("default", "srv", TestContext.Current.CancellationToken);

        result.Status.ShouldBe(McpOperationStatus.Ok);
        result.Resources.Count.ShouldBe(Limit);
        result.Resources[0].Uri.ShouldBe("res://1");
        Volatile.Read(ref pages).ShouldBe(Limit + 1);
    }

    [Fact]
    public async Task A_resource_list_within_the_limit_is_read_to_its_last_page()
    {
        var pages = 0;
        await using var server = PagedServer("resources/list", 3, () => Interlocked.Increment(ref pages), static page => Resource(page));

        var result = await NewResourceClient(server).ListResourcesAsync("default", "srv", TestContext.Current.CancellationToken);

        result.Resources.Count.ShouldBe(3);
        Volatile.Read(ref pages).ShouldBe(3);
    }

    [Fact]
    public async Task A_prompt_list_that_never_ends_is_cut_at_the_limit_after_one_page_past_it()
    {
        var pages = 0;
        await using var server = EndlessServer("prompts/list", () => Interlocked.Increment(ref pages), static page => Prompt(page));

        var result = await NewPromptClient(server).ListPromptsAsync("default", "srv", TestContext.Current.CancellationToken);

        result.Status.ShouldBe(McpOperationStatus.Ok);
        result.Prompts.Count.ShouldBe(Limit);
        result.Prompts[0].Name.ShouldBe("prompt_1");
        Volatile.Read(ref pages).ShouldBe(Limit + 1);
    }

    [Fact]
    public async Task A_resource_past_the_limit_is_not_declared_and_the_search_ends_at_the_limit()
    {
        var pages = 0;
        await using var server = EndlessServer("resources/list", () => Interlocked.Increment(ref pages), static page => Resource(page));

        var (status, content) = await NewResourceClient(server).ReadResourceAsync("default", "srv", $"res://{Limit + 3}", TestContext.Current.CancellationToken);

        status.ShouldBe(McpOperationStatus.UriNotDeclared);
        content.ShouldBeNull();
        Volatile.Read(ref pages).ShouldBe(Limit + 1);
    }

    [Fact]
    public async Task A_declared_resource_is_read_and_the_search_ends_at_the_page_that_holds_it()
    {
        var pages = 0;
        await using var server = EndlessServer(
            "resources/list",
            () => Interlocked.Increment(ref pages),
            static page => Resource(page),
            readResult: "{\"contents\":[{\"uri\":\"res://2\",\"mimeType\":\"text/plain\",\"text\":\"hello\"}]}");

        var (status, content) = await NewResourceClient(server).ReadResourceAsync("default", "srv", "res://2", TestContext.Current.CancellationToken);

        status.ShouldBe(McpOperationStatus.Ok);
        content.ShouldNotBeNull().Text.ShouldBe("hello");
        Volatile.Read(ref pages).ShouldBe(2);
    }

    [Fact]
    public async Task A_resource_list_that_never_ends_does_not_hold_the_catalog_refresh()
    {
        var pages = 0;
        await using var server = EndlessServer("resources/list", () => Interlocked.Increment(ref pages), static page => Resource(page));
        var servers = await StoreAsync(server);

        // The connection limit runs on a clock nobody moves: a pass that kept reading pages until the limit would never end.
        var clock = new ManualClock();

        await using var catalog = new McpToolCatalog(
            servers,
            new InMemoryTenantStore(),
            new EmptyMcpConfiguration(),
            Options.Create(new TraconOptions()),
            Options.Create(McpLimits()),
            NullLoggerFactory.Instance,
            new McpOAuthTokenCacheRegistry(),
            new AllowAllToolAuthorizationHandler(),
            McpToolPolicyTestGates.Open(),
            NoOpToolArgumentsValidator.Instance,
            attribution: null,
            egressGuard: Guard(),
            timeProvider: clock);

        var outcome = await catalog.RefreshAsync(TestContext.Current.CancellationToken).AsTask()
            .WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

        outcome.HadUnreachableServers.ShouldBeFalse("the limit was reached, so the list was not waited for");
        clock.CreatedTimers.ShouldBeGreaterThan(0, "the connection limit was armed on the catalog's clock, and the clock never moved");
        Volatile.Read(ref pages).ShouldBe(Limit + 1);

        // The kept resources make the read_resource tool appear.
        catalog.ForTenant("default").Descriptors.Select(static tool => tool.Name).ShouldContain(static name => name.EndsWith("read_resource", StringComparison.Ordinal));
    }

    [Fact]
    public void The_limits_default_to_a_thousand_resources_and_a_hundred_prompts()
    {
        var defaults = new TraconMcpOptions();

        defaults.MaxResourcesPerServer.ShouldBe(1000);
        defaults.MaxPromptsPerServer.ShouldBe(100);
    }

    private static ScriptedMcpServer EndlessServer(
        string method,
        Func<int> nextPage,
        Func<int, string> item,
        string readResult = "{\"contents\":[]}")
        => ScriptedMcpServer.Start(
            (requested, _, _, _) =>
            {
                if (string.Equals(requested, method, StringComparison.Ordinal))
                {
                    var page = nextPage();

                    return Task.FromResult<string?>(ListOf(method, item(page), $"page-{page}"));
                }

                return Task.FromResult<string?>(string.Equals(requested, "resources/read", StringComparison.Ordinal) ? readResult : "{}");
            },
            Capabilities);

    private static ScriptedMcpServer PagedServer(string method, int lastPage, Func<int> nextPage, Func<int, string> item)
        => ScriptedMcpServer.Start(
            (requested, _, _, _) =>
            {
                if (!string.Equals(requested, method, StringComparison.Ordinal))
                {
                    return Task.FromResult<string?>("{}");
                }

                var page = nextPage();

                return Task.FromResult<string?>(ListOf(method, item(page), page < lastPage ? $"page-{page}" : null));
            },
            Capabilities);

    private static string ListOf(string method, string item, string? cursor)
    {
        var key = string.Equals(method, "resources/list", StringComparison.Ordinal) ? "resources" : "prompts";

        return "{\"" + key + "\":[" + item + "]" + (cursor is null ? string.Empty : ",\"nextCursor\":\"" + cursor + "\"") + "}";
    }

    private static string Resource(int page) => "{\"uri\":\"res://" + page + "\",\"name\":\"resource " + page + "\"}";

    private static string Prompt(int page) => "{\"name\":\"prompt_" + page + "\",\"description\":\"d\"}";

    private static TraconMcpOptions McpLimits()
        => new() { ConnectionTimeout = ConnectionTimeout, MaxResourcesPerServer = Limit, MaxPromptsPerServer = Limit };

    private static EgressSocketGuard Guard() => new(static () => new EgressAddressPolicy(AllowPrivateNetworkTargets: true, AllowLoopback: true));

    private static async Task<InMemoryMcpServerStore> StoreAsync(ScriptedMcpServer server)
    {
        var servers = new InMemoryMcpServerStore();

        await servers.SaveAsync(
            new McpServerDefinition { Id = Guid.NewGuid(), TenantId = "default", Name = "srv", Endpoint = server.Endpoint },
            TestContext.Current.CancellationToken);

        return servers;
    }

    private static McpResourceClient NewResourceClient(ScriptedMcpServer server)
        => new(
            StoreAsync(server).GetAwaiter().GetResult(),
            new EmptyMcpConfiguration(),
            Options.Create(McpLimits()),
            new McpOAuthTokenCacheRegistry(),
            NullLoggerFactory.Instance,
            Options.Create(new TraconOptions()),
            Guard());

    private static McpPromptClient NewPromptClient(ScriptedMcpServer server)
        => new(
            StoreAsync(server).GetAwaiter().GetResult(),
            new EmptyMcpConfiguration(),
            Options.Create(McpLimits()),
            new McpOAuthTokenCacheRegistry(),
            NullLoggerFactory.Instance,
            Options.Create(new TraconOptions()),
            Guard());
}
