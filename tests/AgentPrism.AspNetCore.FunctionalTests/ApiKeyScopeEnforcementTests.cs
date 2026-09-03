using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Verifies with point examples that Family F's four new scopes
/// (<c>PlatformRead</c>, <c>PlatformAdmin</c>, <c>SecurityAdmin</c>,
/// <c>AuditRead</c>) are bound to the right endpoints, and the attenuation
/// boundary holds. <see cref="ApiKeyScopeCoverageTests"/> checks that the
/// ENTIRE scope list is mapped; this file focuses on "the right scope passes,
/// the wrong scope does not" behavior.
/// </summary>
public sealed class ApiKeyScopeEnforcementTests
{
    [Fact]
    public async Task PlatformRead_scoped_key_lists_schedules()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "platform-reader", "PlatformRead");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/schedules");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PlatformRead_scope_is_not_enough_to_write_a_schedule()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "platform-reader", "PlatformRead");

        using var request = new HttpRequestMessage(HttpMethod.Put, "/agentprism/api/schedules/daily")
        {
            Content = JsonContent.Create(new { handlerKey = "agentprism.agent-batch", targetName = "kod-agent", timeZone = "UTC", enabled = true }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PlatformAdmin_scoped_key_writes_a_schedule()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "platform-admin", "PlatformAdmin");

        using var request = new HttpRequestMessage(HttpMethod.Put, "/agentprism/api/schedules/daily")
        {
            Content = JsonContent.Create(
                new { handlerKey = "agentprism.agent-batch", targetName = "kod-agent", timeZone = "UTC", payload = new { }, enabled = true }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SecurityAdmin_scoped_key_can_access_the_key_list()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "security-admin", "SecurityAdmin");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/api-keys");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AuditRead_scoped_key_reads_the_audit_log()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "audit-reader", "AuditRead");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/audit");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RunsRead_cannot_read_the_audit_log()
    {
        // Reuse regression (§7.3.3): AuditRead is a separate scope; it must
        // not silently fall back to RunsRead.
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "runs-reader", "RunsRead");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/audit");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RunsRead_reads_stats_but_cannot_recalculate_costs()
    {
        // §7.3.3: /api/stats requires RunsRead, /api/stats/recalculate-costs
        // requires RunsWrite — two neighboring endpoints in the same file
        // requiring different scopes.
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "runs-reader", "RunsRead");

        using var read = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/stats");
        read.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);
        using var readResponse = await host.Client.SendAsync(read);
        readResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var write = new HttpRequestMessage(HttpMethod.Post, "/agentprism/api/stats/recalculate-costs");
        write.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);
        using var writeResponse = await host.Client.SendAsync(write);
        writeResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PlatformAdmin_scoped_key_cannot_access_the_key_creation_endpoint()
    {
        // §7.3.2: escalation gate — /api/api-keys requires SecurityAdmin,
        // which PlatformAdmin does not cover.
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "platform-admin", "PlatformAdmin");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/agentprism/api/api-keys")
        {
            Content = JsonContent.Create(new { name = "second", scopes = new[] { "PlatformAdmin" } }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- Attenuation (section 53.3) ---

    [Fact]
    public async Task SecurityAdmin_key_cannot_produce_a_scope_it_does_not_itself_carry()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "security-only", "SecurityAdmin");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/agentprism/api/api-keys")
        {
            // The caller carries only SecurityAdmin; requesting RunsWrite must be rejected.
            Content = JsonContent.Create(new { name = "expanded", scopes = new[] { "SecurityAdmin", "RunsWrite" } }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SecurityAdmin_key_can_produce_a_scope_it_itself_carries()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "security-and-runs", "SecurityAdmin", "RunsWrite");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/agentprism/api/api-keys")
        {
            Content = JsonContent.Create(new { name = "subset", scopes = new[] { "RunsWrite" } }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Request_arriving_with_a_static_token_is_outside_attenuation()
    {
        // A request arriving with a static AuthToken/user identity does NOT
        // carry an ApiKeyRecord (ApiKeyRequestContext.Get() returns null);
        // attenuation applies only when an API key validated the request (53.3).
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            "/agentprism/api/api-keys",
            new { name = "root", scopes = new[] { "SecurityAdmin", "PlatformAdmin", "AuditRead" } });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
