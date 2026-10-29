namespace Tracon.Mcp.UnitTests;

/// <summary>The keys the distributed OAuth state uses (K-525: a key is never an interpolated string).</summary>
public sealed class McpOAuthCacheKeysTests
{
    /// <summary>
    /// 🚨 Joined with a separator, <c>("a:b", "c")</c> and <c>("a", "b:c")</c> are the same text: one
    /// tenant would read another tenant's token. The tenant is length-prefixed before it is hashed.
    /// </summary>
    [Theory]
    [InlineData("a:b", "c", "a", "b:c")]
    [InlineData("acme", "_prod", "acme_", "prod")]
    [InlineData("ab", "c", "a", "bc")]
    [InlineData("", "ab", "a", "b")]
    public void Two_different_pairs_never_share_a_key(string firstTenant, string firstServer, string secondTenant, string secondServer)
    {
        var first = new McpTenantServerKey(firstTenant, firstServer);
        var second = new McpTenantServerKey(secondTenant, secondServer);

        string.Equals(McpOAuthCacheKeys.Token(first), McpOAuthCacheKeys.Token(second), StringComparison.Ordinal).ShouldBeFalse();
        string.Equals(McpOAuthCacheKeys.RefreshLease(first), McpOAuthCacheKeys.RefreshLease(second), StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void The_same_pair_always_gives_the_same_key()
    {
        McpOAuthCacheKeys.Token(new McpTenantServerKey("acme", "prod"))
            .ShouldBe(McpOAuthCacheKeys.Token(new McpTenantServerKey("acme", "prod")));
    }

    [Fact]
    public void A_key_carries_no_name_and_no_state()
    {
        const string State = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var pair = new McpTenantServerKey("acme-corp", "payroll-server");

        string[] keys =
        [
            McpOAuthCacheKeys.Token(pair),
            McpOAuthCacheKeys.RefreshLease(pair),
            McpOAuthCacheKeys.Flow(State),
            McpOAuthCacheKeys.Callback(State),
            McpOAuthCacheKeys.Outcome(State),
        ];

        foreach (var key in keys)
        {
            key.ShouldNotContain("acme-corp", Case.Insensitive);
            key.ShouldNotContain("payroll-server", Case.Insensitive);
            key.ShouldNotContain(State, Case.Sensitive);
        }

        keys.Distinct(StringComparer.Ordinal).Count().ShouldBe(keys.Length, "no two uses share a key.");
    }

    [Fact]
    public void A_lease_name_fits_the_narrowest_lease_table()
    {
        // SQL Server's singleton_leases.name is nvarchar(200); a tenant slug and a server name are not bounded by that.
        var longPair = new McpTenantServerKey(new string('t', 300), new string('s', 300));

        McpOAuthCacheKeys.RefreshLease(longPair).Length.ShouldBeLessThan(200);
    }

    [Fact]
    public void The_three_states_of_one_flow_have_three_different_keys()
    {
        const string State = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

        string.Equals(McpOAuthCacheKeys.Flow(State), McpOAuthCacheKeys.Callback(State), StringComparison.Ordinal).ShouldBeFalse();
        string.Equals(McpOAuthCacheKeys.Callback(State), McpOAuthCacheKeys.Outcome(State), StringComparison.Ordinal).ShouldBeFalse();
    }
}
