using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// CRUD and validation behavior of the <c>/api/tenants/{tenantId}/providers</c>
/// and <c>/api/tenants/{tenantId}/egress</c> endpoints (phase 65, BYOK).
/// </summary>
/// <remarks>
/// 🚨 The rule these tests guard above all others: no response, under any
/// condition, carries a credential value (section 65.1/65.5).
/// </remarks>
public sealed class TenantProviderEndpointTests
{
    [Fact]
    public async Task Saved_binding_is_read_back_with_resolved_false_when_the_key_has_no_value()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            "/agentprism/api/tenants/acme/providers/openai",
            new { apiKeyConfigurationName = "AgentPrism:ProviderKeys:Acme:OpenAI" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var saved = await response.Content.ReadFromJsonAsync<TenantProviderBindingResponse>();

        saved.ShouldNotBeNull();
        saved.ProviderName.ShouldBe("openai");
        saved.ApiKeyConfigurationName.ShouldBe("AgentPrism:ProviderKeys:Acme:OpenAI");
        saved.Resolved.ShouldBeFalse();
    }

    [Fact]
    public async Task Resolved_is_true_once_the_configuration_key_carries_a_value()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: services => services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder()
                    .AddInMemoryCollection([new("AgentPrism:ProviderKeys:Acme:OpenAI", "sk-test")])
                    .Build()));

        using var response = await host.Client.PutAsJsonAsync(
            "/agentprism/api/tenants/acme/providers/openai",
            new { apiKeyConfigurationName = "AgentPrism:ProviderKeys:Acme:OpenAI" });

        var saved = await response.Content.ReadFromJsonAsync<TenantProviderBindingResponse>();

        saved.ShouldNotBeNull();
        saved.Resolved.ShouldBeTrue();
    }

    [Fact]
    public async Task Response_never_carries_a_credential_value()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: services => services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder()
                    .AddInMemoryCollection([new("AgentPrism:ProviderKeys:Acme:OpenAI", "sk-super-secret-value")])
                    .Build()));

        await host.Client.PutAsJsonAsync(
            "/agentprism/api/tenants/acme/providers/openai",
            new { apiKeyConfigurationName = "AgentPrism:ProviderKeys:Acme:OpenAI" });

        using var listResponse = await host.Client.GetAsync(
            new Uri("/agentprism/api/tenants/acme/providers", UriKind.Relative));

        var body = await listResponse.Content.ReadAsStringAsync();

        body.ShouldNotContain("sk-super-secret-value");
    }

    [Fact]
    public async Task Name_outside_the_allowed_prefix_is_rejected()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            "/agentprism/api/tenants/acme/providers/openai",
            new { apiKeyConfigurationName = "ConnectionStrings:Default" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Empty_name_is_rejected()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            "/agentprism/api/tenants/acme/providers/openai",
            new { apiKeyConfigurationName = "" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upsert_replaces_the_existing_binding()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        await host.Client.PutAsJsonAsync(
            "/agentprism/api/tenants/acme/providers/openai",
            new { apiKeyConfigurationName = "AgentPrism:ProviderKeys:Acme:First" });

        await host.Client.PutAsJsonAsync(
            "/agentprism/api/tenants/acme/providers/openai",
            new { apiKeyConfigurationName = "AgentPrism:ProviderKeys:Acme:Second" });

        var bindings = await host.Client.GetFromJsonAsync<IReadOnlyList<TenantProviderBindingResponse>>(
            new Uri("/agentprism/api/tenants/acme/providers", UriKind.Relative));

        bindings.ShouldNotBeNull().ShouldHaveSingleItem()
            .ApiKeyConfigurationName.ShouldBe("AgentPrism:ProviderKeys:Acme:Second");
    }

    [Fact]
    public async Task Deleting_an_existing_binding_returns_204()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        await host.Client.PutAsJsonAsync(
            "/agentprism/api/tenants/acme/providers/openai",
            new { apiKeyConfigurationName = "AgentPrism:ProviderKeys:Acme:OpenAI" });

        using var response = await host.Client.DeleteAsync(
            new Uri("/agentprism/api/tenants/acme/providers/openai", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var bindings = await host.Client.GetFromJsonAsync<IReadOnlyList<TenantProviderBindingResponse>>(
            new Uri("/agentprism/api/tenants/acme/providers", UriKind.Relative));

        bindings.ShouldNotBeNull().ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleting_an_unknown_binding_returns_404()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.DeleteAsync(
            new Uri("/agentprism/api/tenants/acme/providers/openai", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Tenant_with_no_saved_egress_policy_is_unrestricted()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var policy = await host.Client.GetFromJsonAsync<TenantEgressPolicyResponse>(
            new Uri("/agentprism/api/tenants/acme/egress", UriKind.Relative));

        policy.ShouldNotBeNull();
        policy.AllowedProviders.ShouldBeNull();
    }

    [Fact]
    public async Task Saved_egress_policy_is_read_back()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            "/agentprism/api/tenants/acme/egress",
            new { allowedProviders = new[] { "openai", "anthropic" } });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var policy = await host.Client.GetFromJsonAsync<TenantEgressPolicyResponse>(
            new Uri("/agentprism/api/tenants/acme/egress", UriKind.Relative));

        policy.ShouldNotBeNull();
        policy.AllowedProviders.ShouldBe(["openai", "anthropic"]);
    }

    [Fact]
    public async Task Binding_a_provider_the_egress_policy_does_not_allow_is_rejected()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        await host.Client.PutAsJsonAsync(
            "/agentprism/api/tenants/acme/egress",
            new { allowedProviders = new[] { "anthropic" } });

        using var response = await host.Client.PutAsJsonAsync(
            "/agentprism/api/tenants/acme/providers/openai",
            new { apiKeyConfigurationName = "AgentPrism:ProviderKeys:Acme:OpenAI" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Deleting_the_egress_policy_returns_the_tenant_to_unrestricted()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        await host.Client.PutAsJsonAsync(
            "/agentprism/api/tenants/acme/egress",
            new { allowedProviders = new[] { "openai" } });

        using var response = await host.Client.DeleteAsync(
            new Uri("/agentprism/api/tenants/acme/egress", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var policy = await host.Client.GetFromJsonAsync<TenantEgressPolicyResponse>(
            new Uri("/agentprism/api/tenants/acme/egress", UriKind.Relative));

        policy.ShouldNotBeNull();
        policy.AllowedProviders.ShouldBeNull();
    }

    [Fact]
    public async Task Deleting_an_unsaved_egress_policy_returns_404()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.DeleteAsync(
            new Uri("/agentprism/api/tenants/acme/egress", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Binding_a_provider_the_egress_policy_allows_succeeds()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        await host.Client.PutAsJsonAsync(
            "/agentprism/api/tenants/acme/egress",
            new { allowedProviders = new[] { "openai" } });

        using var response = await host.Client.PutAsJsonAsync(
            "/agentprism/api/tenants/acme/providers/openai",
            new { apiKeyConfigurationName = "AgentPrism:ProviderKeys:Acme:OpenAI" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Saving_a_binding_writes_an_audit_entry_without_a_credential_value()
    {
        // The default test host's ambient tenant is "default" (AgentPrismOptions.DefaultTenantId);
        // /api/audit/{entity} filters by that ambient tenant, so the path tenant is matched to it here.
        await using var host = await AgentPrismTestHost.StartAsync();

        await host.Client.PutAsJsonAsync(
            "/agentprism/api/tenants/default/providers/openai",
            new { apiKeyConfigurationName = "AgentPrism:ProviderKeys:Default:OpenAI" });

        using var response = await host.Client.GetAsync(
            new Uri("/agentprism/api/audit/tenant_provider:default:openai", UriKind.Relative));

        var entries = await response.Content.ReadFromJsonAsync<IReadOnlyList<AuditEntry>>();

        var entry = entries.ShouldNotBeNull().ShouldHaveSingleItem();
        entry.Action.ShouldBe("tenant_provider.save");
        entry.After.ShouldNotBeNull();
        entry.After.ShouldContain("AgentPrism:ProviderKeys:Default:OpenAI");
    }

    [Fact]
    public async Task Deleting_a_binding_writes_an_audit_entry()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        await host.Client.PutAsJsonAsync(
            "/agentprism/api/tenants/default/providers/openai",
            new { apiKeyConfigurationName = "AgentPrism:ProviderKeys:Default:OpenAI" });

        await host.Client.DeleteAsync(new Uri("/agentprism/api/tenants/default/providers/openai", UriKind.Relative));

        using var response = await host.Client.GetAsync(
            new Uri("/agentprism/api/audit/tenant_provider:default:openai", UriKind.Relative));

        var entries = await response.Content.ReadFromJsonAsync<IReadOnlyList<AuditEntry>>();

        var actions = entries.ShouldNotBeNull().Select(static entry => entry.Action).ToList();
        actions.ShouldContain("tenant_provider.delete", StringComparer.Ordinal);
    }
}
