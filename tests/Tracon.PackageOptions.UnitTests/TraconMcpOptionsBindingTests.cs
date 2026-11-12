namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:Mcp</c> reaches <see cref="TraconMcpOptions"/>.
/// </summary>
/// <remarks>
/// <see cref="McpOAuthTokenStore.DistributedCache"/> is the only value that differs from the default, and the validator
/// refuses it without a shared cache and content protection; the options are read without the validators, as the test
/// is about the reading. The reading is also proved in Tracon.Mcp.UnitTests. That test lists the keys by hand and did not name
/// <c>StaleToolRetention</c>; this one fails on a member no key names.
/// </remarks>
public sealed class TraconMcpOptionsBindingTests
{
    [Fact]
    public void Every_member_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Mcp:Enabled"] = "false",
            ["Tracon:Mcp:RefreshInterval"] = "00:10:00",
            ["Tracon:Mcp:StaleToolRetention"] = "00:20:00",
            ["Tracon:Mcp:ConnectionTimeout"] = "00:00:45",
            ["Tracon:Mcp:MaxToolsPerServer"] = "7",
            ["Tracon:Mcp:MaxResponseBytes"] = "2097152",
            ["Tracon:Mcp:MaxResourcesPerServer"] = "11",
            ["Tracon:Mcp:MaxPromptsPerServer"] = "13",
            ["Tracon:Mcp:MaxResourceBytesPerResource"] = "1024",
            ["Tracon:Mcp:MaxResourceBytesTotal"] = "4096",
            ["Tracon:Mcp:OAuthCallbackBaseUri"] = "https://myapp.example.com/",
            ["Tracon:Mcp:OAuthTokenStore"] = "DistributedCache",
            ["Tracon:Mcp:OAuthTokenSlidingLifetime"] = "7.00:00:00",
        };

        var options = SectionBinding.BindWithoutValidation<TraconMcpOptions>(
            values, static (builder, tracon) => builder.UseMcp(tracon.GetSection("Mcp"), configure: null));

        options.Enabled.ShouldBeFalse();
        options.RefreshInterval.ShouldBe(TimeSpan.FromMinutes(10));
        options.StaleToolRetention.ShouldBe(TimeSpan.FromMinutes(20));
        options.ConnectionTimeout.ShouldBe(TimeSpan.FromSeconds(45));
        options.MaxToolsPerServer.ShouldBe(7);
        options.MaxResponseBytes.ShouldBe(2097152);
        options.MaxResourcesPerServer.ShouldBe(11);
        options.MaxPromptsPerServer.ShouldBe(13);
        options.MaxResourceBytesPerResource.ShouldBe(1024);
        options.MaxResourceBytesTotal.ShouldBe(4096);
        options.OAuthCallbackBaseUri.ShouldBe(new Uri("https://myapp.example.com/"));
        options.OAuthTokenStore.ShouldBe(McpOAuthTokenStore.DistributedCache);
        options.OAuthTokenSlidingLifetime.ShouldBe(TimeSpan.FromDays(7));
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:Mcp", options);
    }
}
