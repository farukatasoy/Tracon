using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A stored record may name only a configuration key that belongs to its OWN
/// tenant, on all four surfaces that resolve a secret by name: MCP servers,
/// tenant provider bindings, webhook subscriptions and inbound triggers.
/// </summary>
/// <remarks>
/// <para>
/// The allowed prefix is one per installation. Before this rule, tenant A
/// could save a record that names tenant B's key (or the installation's own
/// flat key) and point it at an address A controls; Tracon then sent B's
/// value there. The rule: a non-default tenant names keys under
/// <c>{prefix}{tenant}:</c>; a flat name directly under the prefix belongs
/// to the default tenant.
/// </para>
/// <para>
/// These live at the HTTP boundary on purpose: the tenant comes from the
/// request, and a unit test of the guard cannot prove that each endpoint
/// passes the RIGHT tenant to it.
/// </para>
/// </remarks>
public sealed class TenantScopedConfigurationKeyTests
{
    private const string TenantHeader = "X-Tracon-Tenant";

    private static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(static builder => builder.UseTenancy(static options =>
        {
            options.Enabled = true;
            options.AllowHeaderResolution = true;
        }));

    private static async Task<HttpResponseMessage> PutAsync(
        TraconTestHost host,
        string path,
        object body,
        string? tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        };

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        return await host.Client.SendAsync(request);
    }

    private static object McpServer(string key) => new
    {
        endpoint = "https://mcp.example.com/",
        transport = "StreamableHttp",
        authorizationConfigurationKey = key,
    };

    // ---- MCP servers -------------------------------------------------------------

    [Theory]
    [InlineData("Tracon:McpSecrets:globex:Token")]
    [InlineData("Tracon:McpSecrets:GithubToken")]
    [InlineData("Tracon:McpSecrets:default:GithubToken")]
    public async Task Mcp_key_outside_the_callers_tenant_is_rejected(string key)
    {
        await using var host = await StartAsync();

        using var response = await PutAsync(host, "/tracon/api/mcp-servers/probe", McpServer(key), "acme");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();

        // The message is the upgrade path: it names the field and the
        // namespace the key has to move to.
        body.ShouldContain("authorizationConfigurationKey");
        body.ShouldContain("Tracon:McpSecrets:acme:");
    }

    [Fact]
    public async Task Mcp_oauth_client_secret_key_outside_the_callers_tenant_is_rejected()
    {
        await using var host = await StartAsync();

        using var response = await PutAsync(
            host,
            "/tracon/api/mcp-servers/probe",
            new
            {
                endpoint = "https://mcp.example.com/",
                transport = "StreamableHttp",
                oauthEnabled = true,
                oauthClientId = "client",
                oauthClientSecretConfigurationKey = "Tracon:McpSecrets:globex:ClientSecret",
            },
            "acme");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("oauthClientSecretConfigurationKey");
    }

    [Theory]
    [InlineData("Tracon:McpSecrets:acme:Token")]
    [InlineData("Tracon:McpSecrets:Acme:Token")]
    public async Task Mcp_key_under_the_callers_tenant_is_accepted(string key)
    {
        await using var host = await StartAsync();

        using var response = await PutAsync(host, "/tracon/api/mcp-servers/probe", McpServer(key), "acme");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// A single-tenant installation keeps working unchanged: its records
    /// belong to the default tenant, which owns the flat names.
    /// </summary>
    [Theory]
    [InlineData("Tracon:McpSecrets:GithubToken")]
    [InlineData("Tracon:McpSecrets:default:GithubToken")]
    public async Task Default_tenant_keeps_flat_and_own_segment_names(string key)
    {
        await using var host = await StartAsync();

        using var response = await PutAsync(host, "/tracon/api/mcp-servers/probe", McpServer(key), tenant: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Default_tenant_cannot_name_another_tenants_key()
    {
        await using var host = await StartAsync();

        using var response = await PutAsync(
            host,
            "/tracon/api/mcp-servers/probe",
            McpServer("Tracon:McpSecrets:acme:Token"),
            tenant: null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---- Tenant provider bindings ------------------------------------------------

    [Theory]
    [InlineData("Tracon:ProviderKeys:globex:OpenAI")]
    [InlineData("Tracon:ProviderKeys:OpenAI")]
    public async Task Provider_key_outside_the_bound_tenant_is_rejected(string key)
    {
        await using var host = await StartAsync();

        using var response = await PutAsync(
            host,
            "/tracon/api/tenants/acme/providers/openai",
            new { apiKeyConfigurationName = key },
            "acme");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();

        body.ShouldContain("apiKeyConfigurationName");
        body.ShouldContain("Tracon:ProviderKeys:acme:");
    }

    [Fact]
    public async Task Provider_key_under_the_bound_tenant_is_accepted()
    {
        await using var host = await StartAsync();

        using var response = await PutAsync(
            host,
            "/tracon/api/tenants/acme/providers/openai",
            new { apiKeyConfigurationName = "Tracon:ProviderKeys:Acme:OpenAI" },
            "acme");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- Webhook subscriptions ---------------------------------------------------

    [Theory]
    [InlineData("Tracon:WebhookSecrets:globex:orders")]
    [InlineData("Tracon:WebhookSecrets:orders")]
    public async Task Webhook_secret_key_outside_the_callers_tenant_is_rejected(string key)
    {
        await using var host = await StartAsync();

        using var response = await PutAsync(
            host,
            "/tracon/api/webhooks/orders",
            new
            {
                url = "https://hooks.example.com/orders",
                events = new[] { WebhookEvents.RunCompleted },
                secretConfigurationKey = key,
            },
            "acme");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Tracon:WebhookSecrets:acme:");
    }

    [Fact]
    public async Task Webhook_secret_key_under_the_callers_tenant_is_accepted()
    {
        await using var host = await StartAsync();

        using var response = await PutAsync(
            host,
            "/tracon/api/webhooks/orders",
            new
            {
                url = "https://hooks.example.com/orders",
                events = new[] { WebhookEvents.RunCompleted },
                secretConfigurationKey = "Tracon:WebhookSecrets:acme:orders",
            },
            "acme");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- Header configuration keys (MCP servers and webhooks) --------------------

    /// <summary>
    /// Every entry of <c>headerConfigurationKeys</c> passes the same rule as the
    /// single key fields; the detail names the entry and the caller's namespace.
    /// </summary>
    [Theory]
    [InlineData("/tracon/api/mcp-servers/probe", "Tracon:McpSecrets:globex:SearchKey", "Tracon:McpSecrets:acme:")]
    [InlineData("/tracon/api/mcp-servers/probe", "Tracon:McpSecrets:SearchKey", "Tracon:McpSecrets:acme:")]
    [InlineData("/tracon/api/mcp-servers/probe", "ConnectionStrings:Default", "Tracon:McpSecrets:")]
    [InlineData("/tracon/api/webhooks/orders", "Tracon:WebhookSecrets:globex:OrdersKey", "Tracon:WebhookSecrets:acme:")]
    [InlineData("/tracon/api/webhooks/orders", "Tracon:WebhookSecrets:OrdersKey", "Tracon:WebhookSecrets:acme:")]
    public async Task Header_key_outside_the_callers_tenant_is_rejected(string path, string key, string expectedSpace)
    {
        await using var host = await StartAsync();

        using var response = await PutAsync(host, path, HeaderKeyBody(path, key), "acme");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("headerConfigurationKeys[X-API-Key]");
        body.ShouldContain(expectedSpace);
    }

    [Theory]
    [InlineData("/tracon/api/mcp-servers/probe", "Tracon:McpSecrets:acme:SearchKey")]
    [InlineData("/tracon/api/webhooks/orders", "Tracon:WebhookSecrets:Acme:OrdersKey")]
    public async Task Header_key_under_the_callers_tenant_is_accepted(string path, string key)
    {
        await using var host = await StartAsync();

        using var response = await PutAsync(host, path, HeaderKeyBody(path, key), "acme");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static object HeaderKeyBody(string path, string key)
    {
        var keys = new Dictionary<string, string>(StringComparer.Ordinal) { ["X-API-Key"] = key };

        return path.Contains("webhooks", StringComparison.Ordinal)
            ? new { url = "https://hooks.example.com/orders", events = new[] { WebhookEvents.RunCompleted }, headerConfigurationKeys = keys }
            : new { endpoint = "https://mcp.example.com/", transport = "StreamableHttp", headerConfigurationKeys = keys };
    }

    // ---- Inbound triggers --------------------------------------------------------

    [Theory]
    [InlineData("Tracon:TriggerSecrets:globex:Slack")]
    [InlineData("Tracon:TriggerSecrets:Slack")]
    public async Task Trigger_secret_key_outside_the_callers_tenant_is_rejected(string key)
    {
        await using var host = await StartAsync();

        using var response = await PutAsync(
            host,
            "/tracon/api/triggers/slack",
            new { targetKind = "agent", targetName = "demo", signingSecretConfigurationName = key },
            "acme");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Tracon:TriggerSecrets:acme:");
    }

    [Fact]
    public async Task Trigger_secret_key_under_the_callers_tenant_is_accepted()
    {
        await using var host = await StartAsync();

        using var response = await PutAsync(
            host,
            "/tracon/api/triggers/slack",
            new { targetKind = "agent", targetName = "demo", signingSecretConfigurationName = "Tracon:TriggerSecrets:acme:Slack" },
            "acme");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
