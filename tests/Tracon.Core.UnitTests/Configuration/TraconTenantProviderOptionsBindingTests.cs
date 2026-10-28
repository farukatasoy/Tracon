namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:TenantProviders</c> really reaches <see cref="TraconTenantProviderOptions"/>.
/// </summary>
/// <remarks>
/// The section is bound by hand (K-021, AOT): a key added to the options and not to the binder is
/// silently ignored. <c>RequireEgressPolicy</c> is a fail-closed security switch, so an unbound key
/// would leave a deployment that wrote it believing tenants without a policy are refused (F-254).
/// </remarks>
public sealed class TraconTenantProviderOptionsBindingTests
{
    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:TenantProviders:AllowedConfigurationPrefix"] = "Contoso:ProviderKeys:",
            ["Tracon:TenantProviders:RequireEgressPolicy"] = "true",
        };

        var options = SectionBinding.Bind<TraconTenantProviderOptions>(values);

        string.Equals(options.AllowedConfigurationPrefix, "Contoso:ProviderKeys:", StringComparison.Ordinal).ShouldBeTrue();
        options.RequireEgressPolicy.ShouldBeTrue();
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:TenantProviders", options);
    }

    [Fact]
    public void An_absent_section_leaves_the_shipped_defaults()
    {
        var options = SectionBinding.Bind<TraconTenantProviderOptions>(SectionBinding.NoKeys);

        string.Equals(options.AllowedConfigurationPrefix, "Tracon:ProviderKeys:", StringComparison.Ordinal).ShouldBeTrue();
        options.RequireEgressPolicy.ShouldBeFalse();
    }

    [Fact]
    public void An_unreadable_value_stops_the_host_instead_of_leaving_the_switch_off()
    {
        // A typo such as "yes" must not read as the default "off": a fail-closed switch that silently
        // stays open is worse than one that refuses to start.
        var failure = SectionBinding.FailsAtStartup(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:TenantProviders:RequireEgressPolicy"] = "yes",
        });

        failure.Message.ShouldContain("RequireEgressPolicy");
    }
}
