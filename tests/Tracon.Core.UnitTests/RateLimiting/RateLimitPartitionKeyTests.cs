using System.Text;

namespace Tracon.Core.UnitTests.RateLimiting;

/// <summary>
/// Pins the exact text of every partition key a rate limiter hands to the counter store.
/// </summary>
/// <remarks>
/// The key carries the tenant, so a drift in its shape is a cross-tenant defect, and a control character in
/// it cannot be seen by reading the source. The checks run on the bytes.
/// </remarks>
public sealed class RateLimitPartitionKeyTests
{
    private static readonly Guid TriggerId = new("0b8f5d3c-7a41-4c1e-9d25-3f6a8e1b2c47");

    [Fact]
    public void Each_form_has_the_exact_documented_text()
    {
        RateLimitPartitionKey.ForTenant(RateLimitGroups.Management, "acme").Value.ShouldBe("endpoint:tracon:tenant:acme");
        RateLimitPartitionKey.ForTenant(RateLimitGroups.AgUi, "acme").Value.ShouldBe("endpoint:agui:tenant:acme");
        RateLimitPartitionKey.ForGlobal(RateLimitGroups.Management).Value.ShouldBe("endpoint:tracon:global");
        RateLimitPartitionKey.ForGlobal(RateLimitGroups.AgUi).Value.ShouldBe("endpoint:agui:global");
        RateLimitPartitionKey.ForTrigger(TriggerId).Value.ShouldBe("trigger:0b8f5d3c-7a41-4c1e-9d25-3f6a8e1b2c47");
    }

    [Fact]
    public void No_form_holds_a_control_character()
    {
        var keys = new[]
        {
            RateLimitPartitionKey.ForTenant(RateLimitGroups.Management, "Acme.Corp_1-x"),
            RateLimitPartitionKey.ForTenant(RateLimitGroups.AgUi, "__default"),
            RateLimitPartitionKey.ForGlobal(RateLimitGroups.Management),
            RateLimitPartitionKey.ForGlobal(RateLimitGroups.AgUi),
            RateLimitPartitionKey.ForTrigger(TriggerId),
        };

        foreach (var key in keys)
        {
            var bytes = Encoding.UTF8.GetBytes(key.Value);

            bytes.ShouldAllBe(
                value => value >= 0x20 && value != 0x7F,
                $"'{key.Value}' holds a control byte.");
        }
    }

    [Fact]
    public void The_tenant_is_taken_in_its_canonical_lower_case_form()
    {
        // Tenant identifiers are matched case-insensitively everywhere else. Two letter cases would be two
        // windows on a case-sensitive store, and two windows double the limit.
        RateLimitPartitionKey.ForTenant(RateLimitGroups.Management, "Acme").ShouldBe(
            RateLimitPartitionKey.ForTenant(RateLimitGroups.Management, "acme"));
    }

    [Fact]
    public void Two_different_partitions_never_share_a_key()
    {
        // A tenant named "global" must not land on the global partition, and a tenant must not land on
        // another group's counter. A custom tenant context can return a ':' inside the identifier; the
        // tenant is the last segment and is taken whole, so it cannot reach into the earlier ones.
        var keys = new[]
        {
            RateLimitPartitionKey.ForGlobal(RateLimitGroups.Management),
            RateLimitPartitionKey.ForGlobal(RateLimitGroups.AgUi),
            RateLimitPartitionKey.ForTenant(RateLimitGroups.Management, "global"),
            RateLimitPartitionKey.ForTenant(RateLimitGroups.AgUi, "global"),
            RateLimitPartitionKey.ForTenant(RateLimitGroups.Management, "agui:tenant:global"),
            RateLimitPartitionKey.ForTenant(RateLimitGroups.Management, "acme"),
            RateLimitPartitionKey.ForTenant(RateLimitGroups.AgUi, "acme"),
            RateLimitPartitionKey.ForTrigger(TriggerId),
            RateLimitPartitionKey.ForTrigger(Guid.Empty),
        };

        keys.Select(static key => key.Value).Distinct(StringComparer.Ordinal).Count().ShouldBe(keys.Length);
    }

    [Fact]
    public void A_blank_tenant_or_group_is_refused()
    {
        Should.Throw<ArgumentException>(() => RateLimitPartitionKey.ForTenant(RateLimitGroups.Management, " "));
        Should.Throw<ArgumentException>(() => RateLimitPartitionKey.ForTenant(string.Empty, "acme"));
        Should.Throw<ArgumentException>(() => RateLimitPartitionKey.ForGlobal(" "));
    }
}
