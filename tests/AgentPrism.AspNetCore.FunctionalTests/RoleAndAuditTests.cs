using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 9 — role-based authorization and the audit trail.
/// </summary>
public sealed class RoleAndAuditTests
{
    private static readonly Uri Agents = new("/agentprism/api/agents", UriKind.Relative);

    // --- Role model: legacy behavior when no policy is registered ---

    [Fact]
    public async Task All_endpoints_work_when_no_role_policy_is_registered()
    {
        // No AgentPrism.Reader/Operator/Admin policy was registered.
        // Same rationale as K-042: the role model must not break upgrading setups.
        await using var host = await AgentPrismTestHost.StartAsync();

        using var list = await host.Client.GetAsync(Agents);
        list.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request());
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var deleted = await host.Client.DeleteAsync(new Uri("/agentprism/api/agents/db-agent", UriKind.Relative));
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Write_is_blocked_but_read_works_when_admin_policy_fails()
    {
        // Only the Admin policy is registered; Reader is never registered.
        // Shows both the role split AND the fallback in the same test.
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(AgentPrismPolicies.Admin, static policy => policy.RequireAssertion(static _ => false)));

        using var list = await host.Client.GetAsync(Agents);
        list.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request());
        created.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Write_works_when_admin_policy_succeeds()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(AgentPrismPolicies.Admin, static policy => policy.RequireAssertion(static _ => true)));

        using var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request());
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Operator_can_start_a_run_but_cannot_access_the_admin_endpoint()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(AgentPrismPolicies.Operator, static policy => policy.RequireAssertion(static _ => true))
                .AddPolicy(AgentPrismPolicies.Admin, static policy => policy.RequireAssertion(static _ => false)));

        using var run = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/agents/kod-agent/run", UriKind.Relative),
            new { message = "merhaba" });
        run.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var create = await host.Client.PostAsJsonAsync(Agents, TestData.Request());
        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- RequireRolePolicies: production safety ---

    [Fact]
    public async Task RequireRolePolicies_enabled_fails_startup_when_a_policy_is_missing()
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => AgentPrismTestHost.StartAsync(
                configureEndpoints: static options => options.RequireRolePolicies = true));

        exception.Message.ShouldContain(AgentPrismPolicies.Reader);
        exception.Message.ShouldContain(AgentPrismPolicies.Operator);
        exception.Message.ShouldContain(AgentPrismPolicies.Admin);
    }

    [Fact]
    public async Task RequireRolePolicies_enabled_starts_successfully_when_all_policies_are_defined()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.RequireRolePolicies = true,
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(AgentPrismPolicies.Reader, static policy => policy.RequireAssertion(static _ => true))
                .AddPolicy(AgentPrismPolicies.Operator, static policy => policy.RequireAssertion(static _ => true))
                .AddPolicy(AgentPrismPolicies.Admin, static policy => policy.RequireAssertion(static _ => true)));

        using var response = await host.Client.GetAsync(Agents);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // --- /api/audit ---

    [Fact]
    public async Task Audit_endpoint_is_protected_by_the_admin_policy()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(AgentPrismPolicies.Admin, static policy => policy.RequireAssertion(static _ => false)));

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/audit", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Agent_write_operations_land_in_the_audit_trail()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using (var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request()))
        {
            created.EnsureSuccessStatusCode();
        }

        using (var deleted = await host.Client.DeleteAsync(
            new Uri("/agentprism/api/agents/db-agent", UriKind.Relative)))
        {
            deleted.EnsureSuccessStatusCode();
        }

        using var audit = await host.Client.GetAsync(new Uri("/agentprism/api/audit?entity=agent:db-agent", UriKind.Relative));
        audit.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = (await AgentPrismTestHost.ReadJsonAsync(audit)).EnumerateArray()
            .Select(static entry => entry.GetProperty("action").GetString())
            .ToList();

        entries.ShouldContain(static action => string.Equals(action, "agent.create", StringComparison.Ordinal));
        entries.ShouldContain(static action => string.Equals(action, "agent.delete", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Entity_history_endpoint_returns_a_single_entity()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using (var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request()))
        {
            created.EnsureSuccessStatusCode();
        }

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/audit/agent:db-agent", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = (await AgentPrismTestHost.ReadJsonAsync(response)).EnumerateArray().ToList();

        entries.ShouldHaveSingleItem().GetProperty("entity").GetString().ShouldBe("agent:db-agent");
    }

    // --- /api/stats/recalculate-costs (Phase 20) ---

    [Fact]
    public async Task Recalculate_costs_is_protected_by_the_admin_policy()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(AgentPrismPolicies.Admin, static policy => policy.RequireAssertion(static _ => false)));

        using var response = await host.Client.PostAsync(
            new Uri("/agentprism/api/stats/recalculate-costs", UriKind.Relative),
            content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Recalculate_costs_lands_in_the_audit_trail_on_success()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using (var response = await host.Client.PostAsync(
            new Uri("/agentprism/api/stats/recalculate-costs", UriKind.Relative),
            content: null))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            var json = await AgentPrismTestHost.ReadJsonAsync(response);
            json.GetProperty("runsConsidered").GetInt64().ShouldBe(0);
        }

        using var audit = await host.Client.GetAsync(new Uri("/agentprism/api/audit?entity=runs:*", UriKind.Relative));
        var entries = (await AgentPrismTestHost.ReadJsonAsync(audit)).EnumerateArray()
            .Select(static entry => entry.GetProperty("action").GetString())
            .ToList();

        entries.ShouldContain(static action => string.Equals(action, "stats.recalculate-costs", StringComparison.Ordinal));
    }

    // --- /api/meta role information ---

    [Fact]
    public async Task Meta_reports_all_roles_open_when_no_policy_is_registered()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/meta", UriKind.Relative));
        var roles = (await AgentPrismTestHost.ReadJsonAsync(response)).GetProperty("roles");

        roles.GetProperty("canRead").GetBoolean().ShouldBeTrue();
        roles.GetProperty("canOperate").GetBoolean().ShouldBeTrue();
        roles.GetProperty("canAdminister").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Meta_returns_canAdminister_false_when_admin_policy_fails()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(AgentPrismPolicies.Admin, static policy => policy.RequireAssertion(static _ => false)));

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/meta", UriKind.Relative));
        var roles = (await AgentPrismTestHost.ReadJsonAsync(response)).GetProperty("roles");

        roles.GetProperty("canAdminister").GetBoolean().ShouldBeFalse();
        roles.GetProperty("canRead").GetBoolean().ShouldBeTrue();
    }

    // --- Audit trail hash chain (Phase 64) ---

    [Fact]
    public async Task Verify_reports_valid_for_a_freshly_written_chain()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        // Any admin action writes at least one audit entry (RetentionEndpoints
        // writes on save) — cheaper than reaching into IAuditLog directly.
        await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/retention/run_events", UriKind.Relative),
            new { maxAgeDays = 30 });

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/audit/verify", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        body.GetProperty("status").GetString().ShouldBe("Valid");
        body.GetProperty("entriesChecked").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Verify_reports_valid_with_zero_entries_for_an_empty_tenant()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/audit/verify", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        body.GetProperty("status").GetString().ShouldBe("Valid");
        body.GetProperty("entriesChecked").GetInt32().ShouldBe(0);
    }
}
