using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Tenant resolution and isolation.
/// </summary>
/// <remarks>
/// 🚨 The rule these tests protect: <strong>the header is not proof of identity</strong>.
/// When a claim is configured, the header must never be read; otherwise an
/// authenticated user could access another tenant's data by adding a header.
/// </remarks>
public sealed class TenancyTests
{
    private const string TenantHeader = "X-AgentPrism-Tenant";

    [Fact]
    public async Task Header_is_ignored_when_disabled()
    {
        // Default behavior: multi-tenancy is disabled, every request falls
        // to the default tenant. A single-tenant setup requires no configuration.
        await using var host = await AgentPrismTestHost.StartAsync();

        (await ReadTenantAsync(host, header: "kiraci-b")).ShouldBe("default");
    }

    [Fact]
    public async Task Header_resolution_does_not_work_unless_explicitly_enabled()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.UseTenancy(static options => options.Enabled = true));

        (await ReadTenantAsync(host, header: "kiraci-b")).ShouldBe("default");
    }

    [Fact]
    public async Task Tenant_is_read_when_header_resolution_is_enabled()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        (await ReadTenantAsync(host, header: "kiraci-b")).ShouldBe("kiraci-b");
    }

    [Fact]
    public async Task Malformed_tenant_falls_back_to_default()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        (await ReadTenantAsync(host, header: "tenant b/../admin")).ShouldBe("default");
    }

    [Fact]
    public async Task A_tenant_outside_the_allowlist_does_NOT_fall_back_to_default()
    {
        // When the allowlist is populated, a value not on the list must not
        // fall back to the default tenant: falling back would mean an
        // unauthorized request could see the default tenant's data. The
        // request is REJECTED with 403 instead of SILENTLY falling back
        // (AgentPrismEndpointFilter.CheckTenancyWhitelist).
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
                options.AllowedTenants.Add("kiraci-a");
            }));

        using var rejected = await host.Client.SendAsync(
            Request(HttpMethod.Get, "/agentprism/api/tenants/current", "kiraci-b", body: null));

        rejected.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await ReadTenantAsync(host, header: "kiraci-a")).ShouldBe("kiraci-a");
    }

    [Fact]
    public async Task Header_is_never_read_when_a_claim_is_configured()
    {
        // This is the heart of the security guarantee. When a claim is
        // configured, the header is ignored; an authenticated user cannot
        // switch tenants by adding a header.
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.ClaimType = "tenant_id";
                options.AllowHeaderResolution = true;
            }),
            // The user has NO 'tenant_id' claim: with a claim configured,
            // resolution must fall back to the default and must NOT fall
            // back to the header.
            configureServices: static services => TestAuthenticationHandler.Add(services));

        (await ReadTenantAsync(host, header: "kiraci-b")).ShouldBe("default");
    }

    [Fact]
    public async Task Tenant_data_does_not_leak_to_another_tenant()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        using var created = await host.Client.SendAsync(
            Request(HttpMethod.Put, "/agentprism/api/mcp-servers/secret", "kiraci-a", new
            {
                endpoint = "https://mcp.example.com/mcp",
                enabled = true,
                requiresApproval = true,
            }));

        created.EnsureSuccessStatusCode();

        using var mine = await host.Client.SendAsync(
            Request(HttpMethod.Get, "/agentprism/api/mcp-servers", "kiraci-a", body: null));

        (await mine.Content.ReadFromJsonAsync<List<McpServerDefinition>>())
            .ShouldNotBeNull()
            .ShouldHaveSingleItem()
            .Name.ShouldBe("secret");

        using var theirs = await host.Client.SendAsync(
            Request(HttpMethod.Get, "/agentprism/api/mcp-servers", "kiraci-b", body: null));

        (await theirs.Content.ReadFromJsonAsync<List<McpServerDefinition>>())
            .ShouldNotBeNull()
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task Approval_rule_does_not_leak_to_another_tenant()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        var store = host.Services.GetRequiredService<IToolApprovalRuleStore>();

        await store.AddAsync(new ToolApprovalRule
        {
            Id = AgentPrismId.NewId(),
            TenantId = "kiraci-a",
            ToolName = "cancel_order",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        using var mine = await host.Client.SendAsync(
            Request(HttpMethod.Get, "/agentprism/api/approvals/rules", "kiraci-a", body: null));

        (await mine.Content.ReadFromJsonAsync<List<ToolApprovalRule>>())
            .ShouldNotBeNull()
            .Count.ShouldBe(1);

        using var theirs = await host.Client.SendAsync(
            Request(HttpMethod.Get, "/agentprism/api/approvals/rules", "kiraci-b", body: null));

        (await theirs.Content.ReadFromJsonAsync<List<ToolApprovalRule>>())
            .ShouldNotBeNull()
            .ShouldBeEmpty();
    }

    [Fact]
    public void Tenant_id_format_is_validated()
    {
        HttpTenantContext.IsValidTenantId("acme").ShouldBeTrue();
        HttpTenantContext.IsValidTenantId("acme-1.prod_2").ShouldBeTrue();

        HttpTenantContext.IsValidTenantId(null).ShouldBeFalse();
        HttpTenantContext.IsValidTenantId("").ShouldBeFalse();
        HttpTenantContext.IsValidTenantId("has space").ShouldBeFalse();
        HttpTenantContext.IsValidTenantId("path/traversal").ShouldBeFalse();
        HttpTenantContext.IsValidTenantId(new string('a', 65)).ShouldBeFalse();
    }

    private static async Task<string> ReadTenantAsync(AgentPrismTestHost host, string header)
    {
        using var response = await host.Client.SendAsync(
            Request(HttpMethod.Get, "/agentprism/api/tenants/current", header, body: null));

        response.EnsureSuccessStatusCode();

        var current = await response.Content.ReadFromJsonAsync<CurrentTenantResponse>();

        return current.ShouldNotBeNull().TenantId;
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string tenant, object? body)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));

        request.Headers.Add(TenantHeader, tenant);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }
}
