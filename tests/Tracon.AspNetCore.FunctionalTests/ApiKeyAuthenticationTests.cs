using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Validation of the API key as a second identity source, tenant
/// resolution, and scope enforcement (Phase 53).
/// </summary>
/// <remarks>
/// 🚨 The core rule these tests guard: the tenant is resolved from the key,
/// not from the header, and the header cannot OVERRIDE the key's tenant
/// (section 53.5).
/// </remarks>
public sealed class ApiKeyAuthenticationTests
{
    private const string TenantHeader = "X-Tracon-Tenant";

    // --- Basic validation ---

    [Fact]
    public async Task Request_with_a_valid_key_passes_without_a_header()
    {
        await using var host = await TraconTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "ci", "AgentsRead");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/tracon/api/agents");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unknown_value_gets_401_when_no_static_token_is_configured()
    {
        // 🚨 Deliberate behavior change (K-XXX): when AuthToken is undefined and no
        // header is sent at all, the old behavior (open access) is preserved. But
        // if an Authorization header IS SENT, it must now validate against either
        // the static token or an API key.
        await using var host = await TraconTestHost.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/tracon/api/agents");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "no-such-key");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Old_behavior_is_preserved_when_there_is_no_header_and_no_token_is_configured()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/agents", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Revoked_key_gets_401()
    {
        await using var host = await TraconTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "ci", "AgentsRead");
        (await host.Client.DeleteAsync(
            new Uri($"/tracon/api/api-keys/{created.Record.Id}", UriKind.Relative))).Dispose();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/tracon/api/agents");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Expired_key_gets_401()
    {
        await using var host = await TraconTestHost.StartAsync();

        var store = host.Services.GetRequiredService<IApiKeyStore>();
        var created = await store.CreateAsync(new ApiKeyDraft
        {
            TenantId = "default",
            Name = "ci",
            Scopes = [ApiKeyScope.AgentsRead],
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/tracon/api/agents");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // --- Tenant resolution (section 53.5) ---

    [Fact]
    public async Task Tenant_is_resolved_from_the_key_not_from_the_header()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        var store = host.Services.GetRequiredService<IApiKeyStore>();
        var created = await store.CreateAsync(new ApiKeyDraft
        {
            TenantId = "tenant-a",
            Name = "ci",
            Scopes = [ApiKeyScope.AgentsRead],
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/tracon/api/tenants/current");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var current = await response.Content.ReadFromJsonAsync<CurrentTenantResponse>();
        current.ShouldNotBeNull().TenantId.ShouldBe("tenant-a");
    }

    [Fact]
    public async Task Header_passes_when_it_matches_the_keys_tenant()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        var store = host.Services.GetRequiredService<IApiKeyStore>();
        var created = await store.CreateAsync(new ApiKeyDraft
        {
            TenantId = "tenant-a",
            Name = "ci",
            Scopes = [ApiKeyScope.AgentsRead],
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/tracon/api/agents");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);
        request.Headers.Add(TenantHeader, "tenant-a");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Header_gets_403_when_it_differs_from_the_keys_tenant()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        var store = host.Services.GetRequiredService<IApiKeyStore>();
        var created = await store.CreateAsync(new ApiKeyDraft
        {
            TenantId = "tenant-a",
            Name = "ci",
            Scopes = [ApiKeyScope.AgentsRead],
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/tracon/api/agents");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);
        request.Headers.Add(TenantHeader, "tenant-b");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- Scope enforcement (section 53.3) ---

    [Fact]
    public async Task Insufficiently_scoped_key_gets_403()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "readonly", "RunsRead");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/tracon/api/agents/kod-agent/run")
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Correctly_scoped_key_starts_the_run()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "runner", "RunsWrite");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/tracon/api/agents/kod-agent/run")
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task No_enforcement_on_a_scopeless_endpoint()
    {
        // /api/tenants/current is the ONLY scopeless endpoint BEHIND the protected
        // group (§7.2, Family F) — it must be reachable with any scoped key.
        await using var host = await TraconTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "reader", "AgentsRead");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/tracon/api/tenants/current");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Wrong_scope_on_a_scoped_endpoint_returns_403()
    {
        // /api/api-keys now requires SecurityAdmin (Family F); a key scoped
        // to AgentsRead cannot access it.
        await using var host = await TraconTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "reader", "AgentsRead");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/tracon/api/api-keys");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- External surface lockdown (section 53.4) ---

    [Fact]
    public async Task MCP_cannot_start_when_AllowRemoteAccess_is_on_and_there_is_no_external_invoke_key()
    {
        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await using var host = await TraconTestHost.StartAsync(
                configureTracon: static builder => builder
                    .AddAgent(TestData.Definition())
                    .UseMcpServer(),
                configureEndpoints: static options => options.AllowRemoteAccess = true,
                configureAfterMap: static app => app.MapTraconMcpServer());
        });
    }

    /// <summary>
    /// 🚨 The startup guard only proves a key EXISTS. Before this test the request
    /// itself was never asked for one: with AllowRemoteAccess on, no AuthToken and
    /// no AuthorizationPolicy, an unauthenticated POST to the MCP surface answered
    /// 200. An agent surface published to the internet was open.
    /// </summary>
    [Fact]
    public async Task MCP_rejects_a_request_that_presents_no_api_key_when_published()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(static options => options.ExposedAgents.Add("kod-agent")),
            configureEndpoints: static options => options.AllowRemoteAccess = true,
            configureAfterMap: static app =>
            {
                var store = app.Services.GetRequiredService<IApiKeyStore>();

                store.CreateAsync(new ApiKeyDraft
                {
                    TenantId = "default",
                    Name = "mcp",
                    Scopes = [ApiKeyScope.ExternalInvoke],
                }).AsTask().GetAwaiter().GetResult();

                app.MapTraconMcpServer();
            });

        var (response, _) = await McpTestClient.SendAsync(host.Client, "/tracon/mcp", "tools/list");

        response.StatusCode.ShouldBe(
            HttpStatusCode.Unauthorized,
            "the external:invoke key has to be PRESENTED, not merely exist somewhere.");
    }

    /// <summary>
    /// The loopback default stays zero-configuration: a local installation that
    /// never published its surface still answers without a key.
    /// </summary>
    [Fact]
    public async Task MCP_still_answers_on_loopback_without_an_api_key()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(static options => options.ExposedAgents.Add("kod-agent")),
            configureAfterMap: static app => app.MapTraconMcpServer());

        var (response, _) = await McpTestClient.SendAsync(host.Client, "/tracon/mcp", "tools/list");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MCP_answers_when_the_external_invoke_key_is_presented()
    {
        string? plaintextKey = null;

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(static options => options.ExposedAgents.Add("kod-agent")),
            configureEndpoints: static options => options.AllowRemoteAccess = true,
            configureAfterMap: app =>
            {
                var store = app.Services.GetRequiredService<IApiKeyStore>();

                plaintextKey = store.CreateAsync(new ApiKeyDraft
                {
                    TenantId = "default",
                    Name = "mcp",
                    Scopes = [ApiKeyScope.ExternalInvoke],
                }).AsTask().GetAwaiter().GetResult().PlaintextKey;

                app.MapTraconMcpServer();
            });

        var (response, _) = await McpTestClient.SendAsync(
            host.Client,
            "/tracon/mcp",
            "tools/list",
            token: plaintextKey);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// A key without <c>external:invoke</c> must not reach the surface either:
    /// the scope check has to run, not just the presence check.
    /// </summary>
    [Fact]
    public async Task MCP_rejects_a_key_that_lacks_the_external_invoke_scope()
    {
        string? plaintextKey = null;

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(static options => options.ExposedAgents.Add("kod-agent")),
            configureEndpoints: static options => options.AllowRemoteAccess = true,
            configureAfterMap: app =>
            {
                var store = app.Services.GetRequiredService<IApiKeyStore>();

                store.CreateAsync(new ApiKeyDraft
                {
                    TenantId = "default",
                    Name = "mcp",
                    Scopes = [ApiKeyScope.ExternalInvoke],
                }).AsTask().GetAwaiter().GetResult();

                plaintextKey = store.CreateAsync(new ApiKeyDraft
                {
                    TenantId = "default",
                    Name = "reader",
                    Scopes = [ApiKeyScope.RunsRead],
                }).AsTask().GetAwaiter().GetResult().PlaintextKey;

                app.MapTraconMcpServer();
            });

        var (response, _) = await McpTestClient.SendAsync(
            host.Client,
            "/tracon/mcp",
            "tools/list",
            token: plaintextKey);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
