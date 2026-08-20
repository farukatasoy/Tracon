using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// The outbound network guard across all three surfaces that reach the
/// network: MCP server registration, tenant provider bindings and webhook
/// subscriptions.
/// </summary>
/// <remarks>
/// These live at the HTTP boundary on purpose. The address rules themselves are
/// pinned by <c>EgressAddressValidatorTests</c>; what needs an HTTP-level test
/// is that the endpoints actually consult them, that the options really bind,
/// and that the rejection reaches the caller as a <c>400</c> whose message
/// names the setting to change.
/// </remarks>
public sealed class EgressGuardTests
{
    private static Task<AgentPrismTestHost> StartAsync(bool allowPrivateNetworkTargets = false)
        => AgentPrismTestHost.StartAsync(configureServices: services =>
            services.Configure<AgentPrismEgressOptions>(
                options => options.AllowPrivateNetworkTargets = allowPrivateNetworkTargets));

    private static Uri McpServer(string name) => new($"/agentprism/api/mcp-servers/{name}", UriKind.Relative);

    // ---- MCP server registration -------------------------------------------------

    [Theory]
    [InlineData("http://169.254.169.254/")]
    [InlineData("http://10.0.0.5:8080/mcp")]
    [InlineData("https://192.168.1.10/mcp")]
    [InlineData("http://127.0.0.1:9000/mcp")]
    [InlineData("http://[64:ff9b::a9fe:a9fe]/mcp")]
    public async Task Mcp_endpoint_pointing_at_a_private_address_is_rejected(string endpoint)
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            McpServer("probe"),
            new { endpoint, transport = "StreamableHttp" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // The message must name the setting to turn on; otherwise an operator
        // whose MCP server really is on the internal network has no way forward.
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldContain(nameof(AgentPrismEgressOptions.AllowPrivateNetworkTargets));
    }

    [Fact]
    public async Task Mcp_endpoint_pointing_at_a_private_address_is_accepted_once_the_setting_is_on()
    {
        await using var host = await StartAsync(allowPrivateNetworkTargets: true);

        using var response = await host.Client.PutAsJsonAsync(
            McpServer("probe"),
            new { endpoint = "http://169.254.169.254/", transport = "StreamableHttp" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>A host NAME is not resolved at save time, so it must still save.</summary>
    [Fact]
    public async Task Mcp_endpoint_with_a_host_name_saves_without_a_dns_lookup()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            McpServer("probe"),
            new { endpoint = "https://mcp.example.com/", transport = "StreamableHttp" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("ConnectionStrings:Default")]
    [InlineData("AgentPrism:Providers:OpenAI:ApiKey")]
    [InlineData("AgentPrism:Mcp:GithubToken")]
    public async Task Mcp_authorization_key_outside_the_allowed_prefix_is_rejected(string key)
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            McpServer("probe"),
            new
            {
                endpoint = "https://mcp.example.com/",
                transport = "StreamableHttp",
                authorizationConfigurationKey = key,
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();

        body.ShouldContain(new AgentPrismMcpSecurityOptions().AllowedConfigurationPrefix);
        body.ShouldContain("authorizationConfigurationKey");
    }

    [Fact]
    public async Task Mcp_oauth_client_secret_key_outside_the_allowed_prefix_is_rejected()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            McpServer("probe"),
            new
            {
                endpoint = "https://mcp.example.com/",
                transport = "StreamableHttp",
                oauthEnabled = true,
                oauthClientId = "client",
                oauthClientSecretConfigurationKey = "ConnectionStrings:Default",
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("oauthClientSecretConfigurationKey");
    }

    [Fact]
    public async Task Mcp_key_under_the_allowed_prefix_is_accepted()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            McpServer("probe"),
            new
            {
                endpoint = "https://mcp.example.com/",
                transport = "StreamableHttp",
                authorizationConfigurationKey = "AgentPrism:McpSecrets:GithubToken",
            });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// The upgrade path: a definition written before the prefix existed can
    /// still be READ, but saving it again is refused with a message that names
    /// the field to correct.
    /// </summary>
    [Fact]
    public async Task Pre_existing_definition_with_an_out_of_prefix_key_reads_but_cannot_be_resaved()
    {
        await using var host = await StartAsync();

        var store = host.Services.GetRequiredService<IMcpServerStore>();

        await store.SaveAsync(
            new McpServerDefinition
            {
                Id = Guid.NewGuid(),
                TenantId = "default",
                Name = "legacy",
                Endpoint = new Uri("https://mcp.example.com/"),
                AuthorizationConfigurationKey = "AgentPrism:Mcp:LegacyToken",
            },
            CancellationToken.None);

        // Reading is untouched: an operator must be able to SEE what to fix.
        var listed = await host.Client.GetFromJsonAsync<List<McpServerDefinition>>(
            new Uri("/agentprism/api/mcp-servers", UriKind.Relative));

        listed.ShouldNotBeNull().ShouldContain(server => server.Name == "legacy");

        using var response = await host.Client.PutAsJsonAsync(
            McpServer("legacy"),
            new
            {
                endpoint = "https://mcp.example.com/",
                transport = "StreamableHttp",
                authorizationConfigurationKey = "AgentPrism:Mcp:LegacyToken",
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("authorizationConfigurationKey");
    }

    /// <summary>Another tenant's definition is judged by the same rule, not exempted.</summary>
    [Fact]
    public async Task Another_tenants_definition_is_judged_by_the_same_rule()
    {
        await using var host = await StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Put, McpServer("probe"))
        {
            Content = JsonContent.Create(new
            {
                endpoint = "http://10.0.0.5/mcp",
                transport = "StreamableHttp",
            }),
        };

        request.Headers.Add("X-AgentPrism-Tenant", "acme");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---- Tenant provider bindings ------------------------------------------------

    [Theory]
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://169.254.169.254/v1")]
    public async Task Tenant_provider_endpoint_pointing_at_a_private_address_is_rejected(string endpoint)
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/tenants/acme/providers/anthropic", UriKind.Relative),
            new { apiKeyConfigurationName = "AgentPrism:ProviderKeys:acme", endpoint });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync())
            .ShouldContain(nameof(AgentPrismEgressOptions.AllowPrivateNetworkTargets));
    }

    [Fact]
    public async Task Tenant_provider_endpoint_is_accepted_once_the_setting_is_on()
    {
        await using var host = await StartAsync(allowPrivateNetworkTargets: true);

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/tenants/acme/providers/anthropic", UriKind.Relative),
            new { apiKeyConfigurationName = "AgentPrism:ProviderKeys:acme", endpoint = "http://10.0.0.5/" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Tenant_provider_endpoint_that_is_not_an_absolute_address_is_rejected()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/tenants/acme/providers/anthropic", UriKind.Relative),
            new { apiKeyConfigurationName = "AgentPrism:ProviderKeys:acme", endpoint = "not-an-address" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Tenant_provider_binding_without_an_endpoint_is_unaffected()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/tenants/acme/providers/anthropic", UriKind.Relative),
            new { apiKeyConfigurationName = "AgentPrism:ProviderKeys:acme" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- Webhook subscriptions ---------------------------------------------------

    [Theory]
    [InlineData("ConnectionStrings:Default")]
    [InlineData("AgentPrism:Webhooks:Secrets:orders")]
    public async Task Webhook_secret_key_outside_the_allowed_prefix_is_rejected(string key)
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/webhooks/orders", UriKind.Relative),
            new
            {
                url = "https://hooks.example.com/orders",
                events = new[] { WebhookEvents.RunCompleted },
                secretConfigurationKey = key,
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();

        body.ShouldContain(new AgentPrismWebhookOptions().AllowedConfigurationPrefix);
        body.ShouldContain("secretConfigurationKey");
    }

    [Fact]
    public async Task Webhook_secret_key_under_the_allowed_prefix_is_accepted()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/webhooks/orders", UriKind.Relative),
            new
            {
                url = "https://hooks.example.com/orders",
                events = new[] { WebhookEvents.RunCompleted },
                secretConfigurationKey = "AgentPrism:WebhookSecrets:orders",
            });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Webhook_without_a_secret_key_is_unaffected()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/webhooks/orders", UriKind.Relative),
            new
            {
                url = "https://hooks.example.com/orders",
                events = new[] { WebhookEvents.RunCompleted },
            });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- The guard itself --------------------------------------------------------

    /// <summary>
    /// The connection callback is the check that a saved-then-rebound name
    /// cannot evade: it runs per connection, on the address the socket is
    /// about to use. Registering the guard is what makes that true, so the
    /// registration is asserted rather than assumed.
    /// </summary>
    [Fact]
    public async Task Guard_is_registered_and_rejects_a_private_target_at_connection_time()
    {
        await using var host = await StartAsync();

        var guard = host.Services.GetRequiredService<EgressSocketGuard>();

        var exception = await Should.ThrowAsync<AgentPrismException>(async () =>
            await guard.ValidateAsync(new Uri("http://169.254.169.254/")));

        exception.Message.ShouldContain(nameof(AgentPrismEgressOptions.AllowPrivateNetworkTargets));
    }

    [Fact]
    public async Task Guard_reflects_the_setting_without_a_restart()
    {
        await using var host = await StartAsync(allowPrivateNetworkTargets: true);

        var guard = host.Services.GetRequiredService<EgressSocketGuard>();

        await Should.NotThrowAsync(async () =>
            await guard.ValidateAsync(new Uri("http://169.254.169.254/")));
    }
}
