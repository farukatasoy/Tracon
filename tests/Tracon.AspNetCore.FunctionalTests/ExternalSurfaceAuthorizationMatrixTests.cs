using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The external surfaces (the MCP server and the A2A server) answer every kind of wrong caller with the refusal that
/// kind deserves, and the one right caller with something else.
/// </summary>
/// <remarks>
/// <para>
/// The operations are read from the endpoint data source: every endpoint that declares an API key mandatory. A route
/// added to either surface later is probed without anyone naming it. The callers are the ways a request goes wrong
/// (no credential, a wrong one, a key without the scope, a revoked key, a tenant header that contradicts the key, a
/// remote address on a loopback installation, a role the policy needs) and the way it goes right.
/// </para>
/// <para>
/// Three installations are probed, because the rules differ: one kept to loopback (the static token is enough there),
/// one published beyond loopback (it is not), and one published behind an authorization policy (the role is judged
/// before the key is looked at).
/// </para>
/// </remarks>
public sealed class ExternalSurfaceAuthorizationMatrixTests
{
    private const string StaticToken = "external-matrix-static-token";
    private const string TenantHeader = "X-Tracon-Tenant";
    private const string CallerRole = "external-caller";
    private const string CallerPolicy = "ExternalCaller";
    private const string RemoteAddress = "203.0.113.9";
    private const string Agent = "kod-agent";

    private enum Outcome
    {
        /// <summary>The credential is missing or wrong.</summary>
        Unauthorized,

        /// <summary>The caller is known and may not.</summary>
        Forbidden,

        /// <summary>The tenant header is not a tenant id.</summary>
        BadRequest,

        /// <summary>Neither a 401 nor a 403, and no server failure: the request reached the surface.</summary>
        Admitted,
    }

    [Fact]
    public async Task On_a_loopback_installation_every_external_operation_judges_its_caller()
    {
        var (host, keys) = await StartAsync(allowRemoteAccess: false, authToken: StaticToken);

        await using (host)
        {
            await AssertMatrixAsync(
                host,
                [
                    new("no credential", null, null, null, null, Outcome.Unauthorized),
                    new("a wrong bearer", "not-a-credential", null, null, null, Outcome.Unauthorized),

                    // On loopback the installation is already confined to the machine, so its own token is enough.
                    new("the static token", StaticToken, null, null, null, Outcome.Admitted),
                    new("the external key", keys.External, null, null, null, Outcome.Admitted),
                    new("a key without the external scope", keys.AllButExternal, null, null, null, Outcome.Forbidden),
                    new("a revoked key", keys.Revoked, null, null, null, Outcome.Unauthorized),
                    new("the key naming another tenant", keys.External, "tenant-b", null, null, Outcome.Forbidden),
                    new("the key naming its own tenant", keys.External, "tenant-a", null, null, Outcome.Admitted),
                    new("a header that is not a tenant id", keys.External, "not a tenant!", null, null, Outcome.BadRequest),
                    new("another tenant's key naming this tenant", keys.OtherTenant, "tenant-a", null, null, Outcome.Forbidden),
                    new("a remote address", keys.External, null, null, RemoteAddress, Outcome.Forbidden),
                ]);
        }
    }

    [Fact]
    public async Task On_a_published_installation_every_external_operation_demands_a_key()
    {
        var (host, keys) = await StartAsync(allowRemoteAccess: true, authToken: StaticToken);

        await using (host)
        {
            await AssertMatrixAsync(
                host,
                [
                    new("no credential", null, null, null, null, Outcome.Unauthorized),
                    new("a wrong bearer", "not-a-credential", null, null, null, Outcome.Unauthorized),

                    // The token names the installation, not a caller, and carries no scope.
                    new("the static token", StaticToken, null, null, null, Outcome.Unauthorized),
                    new("the external key", keys.External, null, null, null, Outcome.Admitted),
                    new("a key without the external scope", keys.AllButExternal, null, null, null, Outcome.Forbidden),
                    new("a revoked key", keys.Revoked, null, null, null, Outcome.Unauthorized),
                    new("the key naming another tenant", keys.External, "tenant-b", null, null, Outcome.Forbidden),
                    new("the key naming its own tenant", keys.External, "tenant-a", null, null, Outcome.Admitted),
                    new("another tenant's key naming this tenant", keys.OtherTenant, "tenant-a", null, null, Outcome.Forbidden),
                    new("a remote address", keys.External, null, null, RemoteAddress, Outcome.Admitted),
                ]);
        }
    }

    [Fact]
    public async Task Behind_an_authorization_policy_the_role_is_judged_before_the_key()
    {
        var (host, keys) = await StartAsync(
            allowRemoteAccess: true,
            authToken: null,
            policy: CallerPolicy,
            configureServices: static services =>
            {
                services
                    .AddAuthentication(EndpointAuthorizationMatrixTests.RoleHeaderAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, EndpointAuthorizationMatrixTests.RoleHeaderAuthenticationHandler>(
                        EndpointAuthorizationMatrixTests.RoleHeaderAuthenticationHandler.SchemeName,
                        configureOptions: null);
                services.AddAuthorizationBuilder().AddPolicy(CallerPolicy, static builder => builder.RequireRole(CallerRole));
            });

        await using (host)
        {
            await AssertMatrixAsync(
                host,
                [
                    new("the key and the role", keys.External, null, CallerRole, null, Outcome.Admitted),
                    new("the key and no role", keys.External, null, null, null, Outcome.Forbidden),
                    new("the key and another role", keys.External, null, "reader", null, Outcome.Forbidden),
                    new("the role and no key", null, null, CallerRole, null, Outcome.Unauthorized),
                    new("the role and a wrong key", "not-a-credential", null, CallerRole, null, Outcome.Unauthorized),
                    new("the role and a key without the scope", keys.AllButExternal, null, CallerRole, null, Outcome.Forbidden),
                    new("the role and a revoked key", keys.Revoked, null, CallerRole, null, Outcome.Unauthorized),
                    new("no role and no key", null, null, null, null, Outcome.Forbidden),
                ]);
        }
    }

    [Fact]
    public async Task Every_route_of_a_host_with_every_surface_is_scoped_or_classified()
    {
        var voice = new StubVoiceProvider();

        await using var host = await OpenApiSnapshotTests.StartFullSurfaceHostAsync(
            configureMoreServices: services =>
            {
                services.AddSingleton<ISpeechTranscriber>(voice);
                services.AddSingleton<ISpeechSynthesizer>(voice);
            },
            configureMoreTracon: static builder => builder
                .UseVoiceConversation()
                .UseMcpServer(static options => options.ExposeAllAgents = true)
                .UseA2A(static options => options.ExposedAgents.Add(Agent))
                .AddAgent(TestData.Definition()),
            configureAfterMap: static app =>
            {
                app.MapTraconMcpServer();
                app.MapTraconA2A();
            });

        var routes = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        var unscoped = routes
            .Where(static route => route.Metadata.GetMetadata<ApiKeyScopeRequirement>() is null)
            .Select(static route => route.RoutePattern.RawText ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .Where(static pattern => !ApiKeyScopeCoverageTests.ExemptRoutePatterns.Contains(pattern)
                && !ApiKeyScopeCoverageTests.ConditionallyMappedExemptRoutePatterns.Contains(pattern)
                && !ConsumerOwnedRoutes.Contains(pattern))
            .ToList();

        routes.Count.ShouldBeGreaterThan(150, "the host lists too few routes for the check to mean anything");

        // Every route is one of: scoped, exempt with a reason, or the consumer's own. What is left has no reason to be open.
        unscoped.ShouldBeEmpty();

        // The two external surfaces and the voice socket are among the scoped routes, and the external ones demand a key.
        routes
            .Where(static route => route.Metadata.GetMetadata<ApiKeyScopeRequirement>() is { Mandatory: true })
            .Select(static route => route.RoutePattern.RawText!)
            .ShouldContain(static pattern => pattern.StartsWith("/tracon/mcp", StringComparison.Ordinal));
        routes
            .Where(static route => route.Metadata.GetMetadata<ApiKeyScopeRequirement>() is { Mandatory: true })
            .Select(static route => route.RoutePattern.RawText!)
            .ShouldContain(static pattern => pattern.StartsWith("/tracon/a2a/", StringComparison.Ordinal));
        routes
            .Select(static route => route.RoutePattern.RawText!)
            .ShouldContain("/tracon/api/voice/sessions/{sessionId}/stream", StringComparer.Ordinal);
    }

    [Fact]
    public async Task An_mcp_caller_sees_and_calls_only_the_tools_of_the_tenant_of_its_key()
    {
        string keyA = null!;
        string keyB = null!;

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                })
                .UseMcpServer(static options => options.ExposeAllAgents = true),
            configureAfterMap: app =>
            {
                keyA = CreateKey(app.Services, "tenant-a", "ext-a", ApiKeyScope.ExternalInvoke).PlaintextKey;
                keyB = CreateKey(app.Services, "tenant-b", "ext-b", ApiKeyScope.ExternalInvoke).PlaintextKey;
                app.MapTraconMcpServer();
            });

        await DefineAgentAsync(host, "tenant-a", "alpha-only");

        var listedA = await McpAsync(host, "tools/list", null, keyA);
        var listedB = await McpAsync(host, "tools/list", null, keyB);

        ToolNames(listedA).ShouldContain("tracon_alpha-only", StringComparer.Ordinal);
        ToolNames(listedB).ShouldNotContain("tracon_alpha-only", StringComparer.Ordinal);

        // Calling the other tenant's tool by name is the same as calling a tool that does not exist.
        var foreign = await McpAsync(host, "tools/call", new { name = "tracon_alpha-only", arguments = new { message = "hello" } }, keyB);

        foreign!.Value.GetProperty("result").GetProperty("isError").GetBoolean().ShouldBeTrue();
        (await RunCountAsync(host, "tenant-a")).ShouldBe(0);
        (await RunCountAsync(host, "tenant-b")).ShouldBe(0);

        var own = await McpAsync(host, "tools/call", new { name = "tracon_alpha-only", arguments = new { message = "hello" } }, keyA);

        (!own!.Value.GetProperty("result").TryGetProperty("isError", out var isError) || !isError.GetBoolean()).ShouldBeTrue();
        (await RunCountAsync(host, "tenant-a")).ShouldBe(1);
        (await RunCountAsync(host, "tenant-b")).ShouldBe(0);
    }

    [Fact]
    public async Task An_mcp_session_opened_by_one_tenant_serves_no_other_tenant()
    {
        string keyA = null!;
        string keyB = null!;

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                })
                .UseMcpServer(static options => options.ExposeAllAgents = true),
            configureAfterMap: app =>
            {
                keyA = CreateKey(app.Services, "tenant-a", "ext-a", ApiKeyScope.ExternalInvoke).PlaintextKey;
                keyB = CreateKey(app.Services, "tenant-b", "ext-b", ApiKeyScope.ExternalInvoke).PlaintextKey;
                app.MapTraconMcpServer();
            });

        await DefineAgentAsync(host, "tenant-a", "alpha-only");

        // Tenant A opens a session. The server is stateless as configured, so it hands back no identifier and a request
        // carries its caller alone; a server that kept sessions would hand one back, and the probe below would then be
        // the one that matters: the same identifier presented under another tenant's key.
        var initialize = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2025-06-18",
                capabilities = new { },
                clientInfo = new { name = "matrix", version = "1" },
            },
        };

        var (opened, _, issued) = await PostMcpAsync(host, initialize, keyA, sessionId: null);

        opened.ShouldBe(HttpStatusCode.OK);

        var sessionId = issued ?? "session-id-nobody-issued";
        var listing = new { jsonrpc = "2.0", id = 2, method = "tools/list" };

        // Another tenant's key presents the identifier.
        var (foreign, foreignBody, _) = await PostMcpAsync(host, listing, keyB, sessionId);

        foreignBody.ShouldNotContain("tracon_alpha-only", Case.Sensitive);
        foreign.ShouldBeOneOf([HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.BadRequest], $"an identifier that is not the caller's was honoured: {foreignBody}");

        // The owner still sees its own tool, with or without the identifier.
        var (own, ownBody, _) = await PostMcpAsync(host, listing, keyA, issued);

        own.ShouldBe(HttpStatusCode.OK);
        ownBody.ShouldContain("tracon_alpha-only", Case.Sensitive);
    }

    [Fact]
    public async Task An_a2a_caller_reaches_only_the_agents_of_the_tenant_of_its_key()
    {
        string keyA = null!;
        string keyB = null!;

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                })
                .UseA2A(static options => options.ExposedAgents.Add("alpha-only")),
            configureAfterMap: app =>
            {
                keyA = CreateKey(app.Services, "tenant-a", "ext-a", ApiKeyScope.ExternalInvoke).PlaintextKey;
                keyB = CreateKey(app.Services, "tenant-b", "ext-b", ApiKeyScope.ExternalInvoke).PlaintextKey;
                app.MapTraconA2A();
            });

        await DefineAgentAsync(host, "tenant-a", "alpha-only");

        var foreign = await SendA2AMessageAsync(host, keyB, "alpha-only");

        foreign.Body.TryGetProperty("result", out _).ShouldBeFalse($"another tenant's agent answered: {foreign.Body}");
        (await RunCountAsync(host, "tenant-a")).ShouldBe(0);
        (await RunCountAsync(host, "tenant-b")).ShouldBe(0);

        var own = await SendA2AMessageAsync(host, keyA, "alpha-only");

        own.Body.TryGetProperty("result", out _).ShouldBeTrue($"the tenant's own agent did not answer: {own.Body}");
        (await RunCountAsync(host, "tenant-a")).ShouldBe(1);
        (await RunCountAsync(host, "tenant-b")).ShouldBe(0);
    }

    /// <summary>
    /// Routes the consumer's application owns, not Tracon: its OpenAPI document. Naming it here is the reason it is not a gap.
    /// </summary>
    private static readonly HashSet<string> ConsumerOwnedRoutes = new(StringComparer.Ordinal)
    {
        "/openapi/{documentName}.json",
    };

    private static async Task<(TraconTestHost Host, Keys Keys)> StartAsync(
        bool allowRemoteAccess,
        string? authToken,
        string? policy = null,
        Action<IServiceCollection>? configureServices = null)
    {
        Keys? keys = null;

        var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                })
                .AddAgent(TestData.Definition())
                .UseMcpServer(static options => options.ExposeAllAgents = true)
                .UseA2A(static options => options.ExposedAgents.Add(Agent)),
            configureEndpoints: options =>
            {
                options.AllowRemoteAccess = allowRemoteAccess;
                options.AuthToken = authToken;

                if (policy is not null)
                {
                    options.RequireAuthorization(policy);
                }
            },
            configureServices: configureServices,
            configureAfterMap: app =>
            {
                var scopes = Enum.GetValues<ApiKeyScope>();
                var revoked = CreateKey(app.Services, "tenant-a", "revoked", ApiKeyScope.ExternalInvoke);

                app.Services.GetRequiredService<IApiKeyStore>()
                    .RevokeAsync("tenant-a", revoked.Record.Id).AsTask().GetAwaiter().GetResult()
                    .ShouldBeTrue();

                keys = new Keys(
                    CreateKey(app.Services, "tenant-a", "external", ApiKeyScope.ExternalInvoke).PlaintextKey,
                    CreateKey(app.Services, "tenant-a", "all-but-external", [.. scopes.Where(static scope => scope != ApiKeyScope.ExternalInvoke)]).PlaintextKey,
                    CreateKey(app.Services, "tenant-b", "other-tenant", ApiKeyScope.ExternalInvoke).PlaintextKey,
                    revoked.PlaintextKey);

                app.MapTraconMcpServer();
                app.MapTraconA2A();
            });

        return (host, keys!);
    }

    private static ApiKeyCreationResult CreateKey(IServiceProvider services, string tenant, string name, params ApiKeyScope[] scopes)
        => services.GetRequiredService<IApiKeyStore>()
            .CreateAsync(new ApiKeyDraft { TenantId = tenant, Name = name, Scopes = scopes })
            .AsTask().GetAwaiter().GetResult();

    private static async Task AssertMatrixAsync(TraconTestHost host, IReadOnlyList<Caller> callers)
    {
        var operations = ExternalOperations(host);

        // Both surfaces, and the agent card, or the generated list lost something.
        operations.Count.ShouldBeGreaterThanOrEqualTo(3);
        operations.Select(static operation => operation.Template).ShouldContain(static template => template.StartsWith("/tracon/mcp", StringComparison.Ordinal));
        operations.Select(static operation => operation.Template).ShouldContain(static template => template.EndsWith("/agent-card.json", StringComparison.Ordinal));
        operations.Select(static operation => operation.Template)
            .ShouldContain(static template => template.StartsWith("/tracon/a2a/", StringComparison.Ordinal) && !template.EndsWith("/agent-card.json", StringComparison.Ordinal));

        var wrong = new List<string>();

        foreach (var operation in operations)
        {
            foreach (var caller in callers)
            {
                var status = await SendAsync(host, operation, caller);

                if (!Matches(caller.Expected, status))
                {
                    wrong.Add($"{operation.Label} for {caller.Name} -> {(int)status}, expected {caller.Expected}");
                }
            }
        }

        wrong.ShouldBeEmpty();
    }

    private static bool Matches(Outcome expected, HttpStatusCode status)
        => expected switch
        {
            Outcome.Unauthorized => status == HttpStatusCode.Unauthorized,
            Outcome.Forbidden => status == HttpStatusCode.Forbidden,
            Outcome.BadRequest => status == HttpStatusCode.BadRequest,
            _ => status is not HttpStatusCode.Unauthorized and not HttpStatusCode.Forbidden && (int)status < 500,
        };

    /// <summary>Lists each method and route whose key requirement is mandatory: the external surfaces.</summary>
    private static List<Operation> ExternalOperations(TraconTestHost host)
    {
        var operations = new List<Operation>();

        foreach (var endpoint in host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            if (endpoint.Metadata.GetMetadata<ApiKeyScopeRequirement>() is not { Mandatory: true })
            {
                continue;
            }

            foreach (var method in endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
            {
                operations.Add(new Operation(method, endpoint.RoutePattern.RawText ?? string.Empty));
            }
        }

        return operations;
    }

    private static async Task<HttpStatusCode> SendAsync(TraconTestHost host, Operation operation, Caller caller)
    {
        using var request = new HttpRequestMessage(new HttpMethod(operation.Method), new Uri(operation.Template, UriKind.Relative));

        if (operation.Method is "POST" or "PUT" or "PATCH")
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");

        if (caller.Bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", caller.Bearer);
        }

        if (caller.Tenant is not null)
        {
            request.Headers.Add(TenantHeader, caller.Tenant);
        }

        if (caller.Role is not null)
        {
            request.Headers.Add(EndpointAuthorizationMatrixTests.RoleHeader, caller.Role);
        }

        if (caller.RemoteAddress is not null)
        {
            request.Headers.Add(TraconTestHost.RemoteIpHeader, caller.RemoteAddress);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            // Headers are enough: a stream that never ends must not hold the matrix.
            using var reply = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

            return reply.StatusCode;
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            // Never answered: the handler ran, so the caller was let in.
            return HttpStatusCode.RequestTimeout;
        }
    }

    private static async Task DefineAgentAsync(TraconTestHost host, string tenant, string name)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/tracon/api/agents", UriKind.Relative))
        {
            Content = JsonContent.Create(TestData.Request(name)),
        };
        request.Headers.Add(TenantHeader, tenant);

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<int> RunCountAsync(TraconTestHost host, string tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/tracon/api/runs", UriKind.Relative));
        request.Headers.Add(TenantHeader, tenant);

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await TraconTestHost.ReadJsonAsync(response)).GetArrayLength();
    }

    /// <summary>Posts one JSON-RPC message to the MCP server, carrying the session identifier when there is one.</summary>
    private static async Task<(HttpStatusCode Status, string Body, string? SessionId)> PostMcpAsync(
        TraconTestHost host,
        object message,
        string key,
        string? sessionId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/tracon/mcp", UriKind.Relative))
        {
            Content = JsonContent.Create(message),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        if (sessionId is not null)
        {
            request.Headers.Add("Mcp-Session-Id", sessionId);
        }

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        return (
            response.StatusCode,
            body,
            response.Headers.TryGetValues("Mcp-Session-Id", out var values) ? values.FirstOrDefault() : null);
    }

    private static async Task<System.Text.Json.JsonElement?> McpAsync(TraconTestHost host, string method, object? parameters, string token)
    {
        var (response, body) = await McpTestClient.SendAsync(host.Client, "/tracon/mcp", method, parameters, token);

        response.Dispose();

        return body;
    }

    private static List<string> ToolNames(System.Text.Json.JsonElement? body)
        => [.. body!.Value.GetProperty("result").GetProperty("tools").EnumerateArray().Select(static tool => tool.GetProperty("name").GetString()!)];

    private static async Task<(HttpStatusCode Status, System.Text.Json.JsonElement Body)> SendA2AMessageAsync(
        TraconTestHost host,
        string key,
        string agent)
    {
        // Built with the SDK's own types, as the other A2A tests do, so the field names are the contract's.
        var message = new A2A.SendMessageRequest
        {
            Message = new A2A.Message
            {
                Role = A2A.Role.User,
                Parts = [A2A.Part.FromText("hello")],
                MessageId = Guid.NewGuid().ToString(),
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/a2a/{agent}", UriKind.Relative))
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "SendMessage",
                @params = System.Text.Json.JsonSerializer.SerializeToElement(message, A2A.A2AJsonUtilities.DefaultOptions),
            }),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        return (response.StatusCode, System.Text.Json.JsonDocument.Parse(text.Length == 0 ? "{}" : text).RootElement.Clone());
    }

    private sealed record Keys(string External, string AllButExternal, string OtherTenant, string Revoked);

    private sealed record Caller(string Name, string? Bearer, string? Tenant, string? Role, string? RemoteAddress, Outcome Expected);

    private sealed record Operation(string Method, string Template)
    {
        public string Label => $"{Method} {Template}";
    }
}
