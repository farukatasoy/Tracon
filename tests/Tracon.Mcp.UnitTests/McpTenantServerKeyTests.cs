namespace Tracon.Mcp.UnitTests;

/// <summary>
/// The <c>(tenant, server)</c> pair that keys the MCP connection cache and the
/// OAuth token cache.
/// </summary>
/// <remarks>
/// 🚨 These tests exist because of a measured defect. The key was built by
/// interpolating the two names into a string. Three of the four call sites put a
/// <c>U+001F</c> separator between them; the fourth - the READ path
/// <c>McpToolCatalog.TryGetConnection</c> - did not. A separator that is an
/// invisible control character cannot be checked by reading the source, so the
/// divergence survived every review: the write path stored
/// <c>"acme␟prod"</c> while the read path looked up <c>"acmeprod"</c>, and the
/// lookup could never hit. <c>McpResourceContextProvider</c> then logged
/// "server is currently unreachable" and dropped the resource - a silent failure
/// blamed on the wrong cause.
/// <para>
/// The key is a typed value now, so a read and a write cannot use different
/// formats. Do not go back to interpolation.
/// </para>
/// </remarks>
public sealed class McpTenantServerKeyTests
{
    [Fact]
    public void Key_keeps_the_two_names_apart()
    {
        // Without a separator both pairs collapse to "acme_prod".
        new McpTenantServerKey("acme", "_prod")
            .ShouldNotBe(new McpTenantServerKey("acme_", "prod"));
    }

    [Fact]
    public void Equal_pairs_are_equal()
    {
        // The cache depends on this: an unequal key would open a new connection
        // on every single lookup.
        new McpTenantServerKey("acme", "prod")
            .ShouldBe(new McpTenantServerKey("acme", "prod"));
    }

    [Fact]
    public void Token_cache_is_shared_within_the_same_pair()
    {
        var registry = new McpOAuthTokenCacheRegistry();

        var first = registry.GetOrCreate("acme", "prod");
        var second = registry.GetOrCreate("acme", "prod");

        // The registry's whole purpose: a token obtained by the interactive OAuth
        // flow must be visible to the catalog's background reconnection.
        ReferenceEquals(first, second).ShouldBeTrue(
            "the same pair must keep sharing one cache; otherwise the OAuth flow's token is lost.");
    }

    [Theory]
    [InlineData("acme", "_prod", "acme_", "prod")]
    [InlineData("acme.x", "-prod", "acme.x-", "prod")]
    public void Token_cache_is_not_shared_between_different_pairs(
        string firstTenant,
        string firstServer,
        string secondTenant,
        string secondServer)
    {
        var registry = new McpOAuthTokenCacheRegistry();

        var first = registry.GetOrCreate(firstTenant, firstServer);
        var second = registry.GetOrCreate(secondTenant, secondServer);

        ReferenceEquals(first, second).ShouldBeFalse(
            $"('{firstTenant}','{firstServer}') and ('{secondTenant}','{secondServer}') are different " +
            "pairs; sharing one token cache would hand the second tenant the first tenant's access token.");
    }

    /// <summary>
    /// The regression that the missing separator actually caused: a connection
    /// stored by the refresh path has to be found by the read path.
    /// </summary>
    [Fact]
    public void Stored_connection_is_found_by_the_read_path()
    {
        var connections = new Dictionary<McpTenantServerKey, string>();

        // The refresh loop stores under the key it builds...
        connections[new McpTenantServerKey("acme", "prod")] = "connection";

        // ...and TryGetConnection looks it up with a key built the same way.
        connections.TryGetValue(new McpTenantServerKey("acme", "prod"), out var found)
            .ShouldBeTrue("the read path must find what the write path stored.");

        found.ShouldBe("connection");
    }
}
