using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 9 — role-based authorization and the audit trail.
/// </summary>
public sealed class RoleAndAuditTests
{
    private static readonly Uri Agents = new("/tracon/api/agents", UriKind.Relative);

    // --- Role model: legacy behavior when no policy is registered ---

    [Fact]
    public async Task All_endpoints_work_when_no_role_policy_is_registered()
    {
        // No Tracon.Reader/Operator/Admin policy was registered.
        // Same rationale as K-042: the role model must not break upgrading setups.
        await using var host = await TraconTestHost.StartAsync();

        using var list = await host.Client.GetAsync(Agents);
        list.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request());
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var deleted = await host.Client.DeleteAsync(new Uri("/tracon/api/agents/db-agent", UriKind.Relative));
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Write_is_blocked_but_read_works_when_admin_policy_fails()
    {
        // Only the Admin policy is registered; Reader is never registered.
        // Shows both the role split AND the fallback in the same test.
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(TraconPolicies.Admin, static policy => policy.RequireAssertion(static _ => false)));

        using var list = await host.Client.GetAsync(Agents);
        list.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request());
        created.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Write_works_when_admin_policy_succeeds()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(TraconPolicies.Admin, static policy => policy.RequireAssertion(static _ => true)));

        using var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request());
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Operator_can_start_a_run_but_cannot_access_the_admin_endpoint()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(TraconPolicies.Operator, static policy => policy.RequireAssertion(static _ => true))
                .AddPolicy(TraconPolicies.Admin, static policy => policy.RequireAssertion(static _ => false)));

        using var run = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/kod-agent/run", UriKind.Relative),
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
            () => TraconTestHost.StartAsync(
                configureEndpoints: static options => options.RequireRolePolicies = true));

        exception.Message.ShouldContain(TraconPolicies.Reader);
        exception.Message.ShouldContain(TraconPolicies.Operator);
        exception.Message.ShouldContain(TraconPolicies.Admin);
    }

    [Fact]
    public async Task RequireRolePolicies_enabled_starts_successfully_when_all_policies_are_defined()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureEndpoints: static options => options.RequireRolePolicies = true,
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(TraconPolicies.Reader, static policy => policy.RequireAssertion(static _ => true))
                .AddPolicy(TraconPolicies.Operator, static policy => policy.RequireAssertion(static _ => true))
                .AddPolicy(TraconPolicies.Admin, static policy => policy.RequireAssertion(static _ => true)));

        using var response = await host.Client.GetAsync(Agents);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_policy_provider_that_throws_at_startup_is_logged_and_keeps_the_fallback()
    {
        // The documented fallback (a missing role policy adds no role check)
        // is kept - but a provider that FAILED is not a provider that said
        // "not registered", and before this the difference was invisible.
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services =>
            {
                services.AddAuthorization();
                services.Replace(ServiceDescriptor.Singleton<IAuthorizationPolicyProvider, ThrowingRolePolicyProvider>());
            });

        var warnings = host.Logs.Entries
            .Where(static line => line.StartsWith("Warning Tracon.RolePolicies ", StringComparison.Ordinal))
            .ToList();

        warnings.Count.ShouldBe(3);
        warnings.ShouldContain(static line => line.Contains(TraconPolicies.Reader, StringComparison.Ordinal));
        warnings.ShouldContain(static line => line.Contains(TraconPolicies.Operator, StringComparison.Ordinal));
        warnings.ShouldContain(static line => line.Contains(TraconPolicies.Admin, StringComparison.Ordinal));
        warnings.ShouldAllBe(static line => line.Contains("role store is unreachable", StringComparison.Ordinal));

        using var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request());
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task RequireRolePolicies_startup_failure_carries_the_provider_exception()
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => TraconTestHost.StartAsync(
                configureEndpoints: static options => options.RequireRolePolicies = true,
                configureServices: static services =>
                {
                    services.AddAuthorization();
                    services.Replace(ServiceDescriptor.Singleton<IAuthorizationPolicyProvider, ThrowingRolePolicyProvider>());
                }));

        exception.Message.ShouldContain(TraconPolicies.Reader);
        exception.InnerException.ShouldNotBeNull().Message.ShouldContain("role store is unreachable");
    }

    // --- /api/audit ---

    [Fact]
    public async Task Audit_endpoint_is_protected_by_the_admin_policy()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(TraconPolicies.Admin, static policy => policy.RequireAssertion(static _ => false)));

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/audit", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Agent_write_operations_land_in_the_audit_trail()
    {
        await using var host = await TraconTestHost.StartAsync();

        using (var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request()))
        {
            created.EnsureSuccessStatusCode();
        }

        using (var deleted = await host.Client.DeleteAsync(
            new Uri("/tracon/api/agents/db-agent", UriKind.Relative)))
        {
            deleted.EnsureSuccessStatusCode();
        }

        using var audit = await host.Client.GetAsync(new Uri("/tracon/api/audit?entity=agent:db-agent", UriKind.Relative));
        audit.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = (await TraconTestHost.ReadJsonAsync(audit)).EnumerateArray()
            .Select(static entry => entry.GetProperty("action").GetString())
            .ToList();

        entries.ShouldContain(static action => string.Equals(action, "agent.create", StringComparison.Ordinal));
        entries.ShouldContain(static action => string.Equals(action, "agent.delete", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Entity_history_endpoint_returns_a_single_entity()
    {
        await using var host = await TraconTestHost.StartAsync();

        using (var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request()))
        {
            created.EnsureSuccessStatusCode();
        }

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/audit/agent:db-agent", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = (await TraconTestHost.ReadJsonAsync(response)).EnumerateArray().ToList();

        entries.ShouldHaveSingleItem().GetProperty("entity").GetString().ShouldBe("agent:db-agent");
    }

    // --- /api/stats/recalculate-costs (Phase 20) ---

    [Fact]
    public async Task Recalculate_costs_is_protected_by_the_admin_policy()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(TraconPolicies.Admin, static policy => policy.RequireAssertion(static _ => false)));

        using var response = await host.Client.PostAsync(
            new Uri("/tracon/api/stats/recalculate-costs", UriKind.Relative),
            content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Recalculate_costs_lands_in_the_audit_trail_on_success()
    {
        await using var host = await TraconTestHost.StartAsync();

        using (var response = await host.Client.PostAsync(
            new Uri("/tracon/api/stats/recalculate-costs", UriKind.Relative),
            content: null))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            var json = await TraconTestHost.ReadJsonAsync(response);
            json.GetProperty("runsConsidered").GetInt64().ShouldBe(0);
            json.GetProperty("runsSkipped").GetInt64().ShouldBe(0);
        }

        using var audit = await host.Client.GetAsync(new Uri("/tracon/api/audit?entity=runs:*", UriKind.Relative));
        var entries = (await TraconTestHost.ReadJsonAsync(audit)).EnumerateArray()
            .Select(static entry => entry.GetProperty("action").GetString())
            .ToList();

        entries.ShouldContain(static action => string.Equals(action, "stats.recalculate-costs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Recalculate_costs_skips_an_already_priced_run_and_fills_in_an_unknown_one()
    {
        // Phase 132, F-175: the maintenance endpoint is a repair tool, not a
        // full recalculation — a run that already carries a known price is
        // untouched, only PricingSource.Unknown rows are candidates. The other
        // recalculate-costs test above proves the audit trail with EMPTY data
        // (runsConsidered/runsSkipped both 0); this one drives the real
        // narrowing behavior through the full HTTP+DI+store chain with a
        // priced row and an unpriced row seeded directly.
        await using var host = await TraconTestHost.StartAsync();
        var runs = host.Services.GetRequiredService<IRunStore>();

        var pricedRunId = await SeedRunAsync(runs, "priced-model", new RunCost
        {
            InputCost = 1m,
            OutputCost = 2m,
            Currency = "USD",
            Source = PricingSource.Catalog,
        });

        var unpricedRunId = await SeedRunAsync(runs, "unpriced-model", new RunCost { Source = PricingSource.Unknown });

        using var response = await host.Client.PostAsync(
            new Uri("/tracon/api/stats/recalculate-costs", UriKind.Relative),
            content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await TraconTestHost.ReadJsonAsync(response);
        json.GetProperty("runsConsidered").GetInt64().ShouldBe(1);
        json.GetProperty("runsSkipped").GetInt64().ShouldBe(1);
        json.GetProperty("runsUpdated").GetInt64().ShouldBe(0);
        json.GetProperty("runsStillUnknown").GetInt64().ShouldBe(1);

        var priced = await runs.GetRunAsync(pricedRunId);
        priced!.Cost!.InputCost.ShouldBe(1m);
        priced.Cost.OutputCost.ShouldBe(2m);

        var unpriced = await runs.GetRunAsync(unpricedRunId);
        unpriced!.Cost!.Source.ShouldBe(PricingSource.Unknown);
    }

    private static async Task<Guid> SeedRunAsync(IRunStore runs, string modelId, RunCost cost)
    {
        var runId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = DateTimeOffset.UtcNow,
            ModelId = modelId,
        });

        await runs.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Usage = new RunUsage { InputTokens = 1_000, OutputTokens = 1_000, TotalTokens = 2_000 },
            Cost = cost,
        });

        return runId;
    }

    // --- /api/meta role information ---

    [Fact]
    public async Task Meta_reports_all_roles_open_when_no_policy_is_registered()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/meta", UriKind.Relative));
        var roles = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("roles");

        roles.GetProperty("canRead").GetBoolean().ShouldBeTrue();
        roles.GetProperty("canOperate").GetBoolean().ShouldBeTrue();
        roles.GetProperty("canAdminister").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Meta_returns_canAdminister_false_when_admin_policy_fails()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(TraconPolicies.Admin, static policy => policy.RequireAssertion(static _ => false)));

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/meta", UriKind.Relative));
        var roles = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("roles");

        roles.GetProperty("canAdminister").GetBoolean().ShouldBeFalse();
        roles.GetProperty("canRead").GetBoolean().ShouldBeTrue();
    }

    // --- Audit trail hash chain (Phase 64) ---

    [Fact]
    public async Task Verify_reports_valid_for_a_freshly_written_chain()
    {
        await using var host = await TraconTestHost.StartAsync();

        // Any admin action writes at least one audit entry (RetentionEndpoints
        // writes on save) — cheaper than reaching into IAuditLog directly.
        await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/retention/run_events", UriKind.Relative),
            new { maxAgeDays = 30 });

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/audit/verify", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await TraconTestHost.ReadJsonAsync(response);
        body.GetProperty("status").GetString().ShouldBe("Valid");
        body.GetProperty("entriesChecked").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Verify_reports_valid_with_zero_entries_for_an_empty_tenant()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/audit/verify", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await TraconTestHost.ReadJsonAsync(response);
        body.GetProperty("status").GetString().ShouldBe("Valid");
        body.GetProperty("entriesChecked").GetInt32().ShouldBe(0);
    }

    /// <summary>
    /// A consumer policy provider whose backing store is unreachable for the
    /// Tracon role names and answers every other name normally.
    /// </summary>
    private sealed class ThrowingRolePolicyProvider(IOptions<AuthorizationOptions> options)
        : DefaultAuthorizationPolicyProvider(options)
    {
        public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
            => policyName is TraconPolicies.Reader or TraconPolicies.Operator or TraconPolicies.Admin
                ? throw new InvalidOperationException("the consumer's role store is unreachable")
                : base.GetPolicyAsync(policyName);
    }
}
