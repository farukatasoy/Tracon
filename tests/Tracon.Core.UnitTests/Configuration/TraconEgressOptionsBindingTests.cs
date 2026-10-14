namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:Egress</c> really reaches <see cref="TraconEgressOptions"/>.
/// </summary>
/// <remarks>
/// The section is bound by hand (K-021, AOT): a key added to the options and not to
/// the binder is silently ignored. <see cref="OptionsKeyCoverage"/> fails this test
/// before such a key ships unbound (F-247).
/// </remarks>
public sealed class TraconEgressOptionsBindingTests
{
    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Egress:AllowPrivateNetworkTargets"] = "true",
        };

        var options = SectionBinding.Bind<TraconEgressOptions>(values);

        options.AllowPrivateNetworkTargets.ShouldBeTrue();
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:Egress", options);
    }

    [Fact]
    public void An_absent_section_leaves_the_shipped_defaults()
    {
        var options = SectionBinding.Bind<TraconEgressOptions>(SectionBinding.NoKeys);

        options.AllowPrivateNetworkTargets.ShouldBeFalse();
    }
}
