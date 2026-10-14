namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:SessionOwnership</c> really reaches <see cref="TraconSessionOwnershipOptions"/>.
/// </summary>
/// <remarks>
/// The section is bound by hand (K-021, AOT): a key added to the options and not to
/// the binder is silently ignored. <see cref="OptionsKeyCoverage"/> fails this test
/// before such a key ships unbound (F-247).
/// </remarks>
public sealed class TraconSessionOwnershipOptionsBindingTests
{
    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:SessionOwnership:Enabled"] = "true",
            ["Tracon:SessionOwnership:RequireAuthenticatedOwner"] = "false",
            ["Tracon:SessionOwnership:ManagementPolicy"] = "Contoso.Admin",
            ["Tracon:SessionOwnership:RefuseUnownedSessions"] = "true",
        };

        var options = SectionBinding.Bind<TraconSessionOwnershipOptions>(values);

        options.Enabled.ShouldBeTrue();
        options.RequireAuthenticatedOwner.ShouldBeFalse();
        string.Equals(options.ManagementPolicy, "Contoso.Admin", StringComparison.Ordinal).ShouldBeTrue();
        options.RefuseUnownedSessions.ShouldBeTrue();
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:SessionOwnership", options);
    }

    [Fact]
    public void An_empty_management_policy_is_kept()
    {
        // 🚨 A security setting: "" is the documented way to say "no caller gets an
        // unfiltered listing". Read as "not configured", it would silently restore
        // the Tracon.Operator default the deployment just tried to remove.
        var options = SectionBinding.Bind<TraconSessionOwnershipOptions>(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:SessionOwnership:ManagementPolicy"] = "",
        });

        options.ManagementPolicy.ShouldBe(string.Empty);
    }

    [Fact]
    public void An_absent_section_leaves_the_shipped_defaults()
    {
        var options = SectionBinding.Bind<TraconSessionOwnershipOptions>(SectionBinding.NoKeys);

        options.Enabled.ShouldBeFalse();
        options.RequireAuthenticatedOwner.ShouldBeTrue();
        string.Equals(options.ManagementPolicy, "Tracon.Operator", StringComparison.Ordinal).ShouldBeTrue();
        options.RefuseUnownedSessions.ShouldBeFalse();
    }
}
