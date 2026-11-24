using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Retention;

/// <summary>
/// <see cref="DefaultRetentionTenantSource"/> names the default tenant, the registered tenants and the tenants that own
/// a retention policy, and no other.
/// </summary>
public sealed class DefaultRetentionTenantSourceTests
{
    [Fact]
    public async Task Names_the_default_tenant_even_when_nothing_is_registered_and_no_policy_exists()
    {
        var (source, _, _) = Build();

        (await ListAsync(source)).ShouldBe(["default"]);
    }

    [Fact]
    public async Task Names_the_registered_tenants()
    {
        var (source, tenants, _) = Build();
        await RegisterAsync(tenants, "acme");
        await RegisterAsync(tenants, "tenant-b");

        (await ListAsync(source)).ShouldBe(["default", "acme", "tenant-b"], ignoreOrder: true);
    }

    [Fact]
    public async Task Names_a_tenant_that_owns_a_policy_although_nobody_registered_it_and_a_switched_off_one_too()
    {
        var (source, _, policies) = Build();
        await SavePolicyAsync(policies, "unregistered", enabled: true);
        await SavePolicyAsync(policies, "paused", enabled: false);

        (await ListAsync(source)).ShouldBe(["default", "unregistered", "paused"], ignoreOrder: true);
    }

    [Fact]
    public async Task The_default_policy_of_every_tenant_is_not_a_tenant()
    {
        var (source, _, policies) = Build();
        await SavePolicyAsync(policies, RetentionPolicyTenant.AllTenants, enabled: true);

        (await ListAsync(source)).ShouldBe(["default"]);
    }

    [Fact]
    public async Task A_tenant_that_is_registered_and_owns_a_policy_may_be_named_twice_and_the_fan_out_queues_it_once()
    {
        var (source, tenants, policies) = Build();
        await RegisterAsync(tenants, "acme");
        await SavePolicyAsync(policies, "acme", enabled: true);

        (await ListAsync(source)).Count(static tenant => string.Equals(tenant, "acme", StringComparison.Ordinal)).ShouldBe(2);
    }

    [Fact]
    public async Task The_configured_default_tenant_is_the_one_named()
    {
        var (source, _, _) = Build(defaultTenant: "installation");

        (await ListAsync(source)).ShouldBe(["installation"]);
    }

    [Fact]
    public async Task A_listing_that_starts_with_a_cancelled_token_stops()
    {
        var (source, _, _) = Build();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var unused in source.ListTenantsAsync(cancelled.Token))
            {
                _ = unused;
            }
        });
    }

    private static (DefaultRetentionTenantSource Source, InMemoryTenantStore Tenants, InMemoryRetentionPolicyStore Policies) Build(string defaultTenant = "default")
    {
        var tenants = new InMemoryTenantStore();
        var policies = new InMemoryRetentionPolicyStore();

        return (
            new DefaultRetentionTenantSource(
                tenants,
                policies,
                new StaticOptionsMonitor<TraconOptions>(new TraconOptions { DefaultTenantId = defaultTenant })),
            tenants,
            policies);
    }

    private static async Task<List<string>> ListAsync(DefaultRetentionTenantSource source)
    {
        var names = new List<string>();

        await foreach (var name in source.ListTenantsAsync(TestContext.Current.CancellationToken))
        {
            names.Add(name);
        }

        return names;
    }

    private static async Task RegisterAsync(InMemoryTenantStore store, string slug)
        => await store.SaveAsync(
            new TenantDescriptor { Id = Guid.NewGuid(), Slug = slug, DisplayName = slug },
            TestContext.Current.CancellationToken);

    private static async Task SavePolicyAsync(InMemoryRetentionPolicyStore store, string tenantId, bool enabled)
    {
        var now = DateTimeOffset.UtcNow;

        await store.SavePolicyAsync(
            new RetentionPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Target = RetentionTargets.RunEvents,
                MaxAgeDays = 30,
                Enabled = enabled,
                CreatedAt = now,
                UpdatedAt = now,
            },
            TestContext.Current.CancellationToken);
    }
}
