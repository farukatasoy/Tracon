namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:ContentGuard</c> really reaches <see cref="TraconContentGuardOptions"/>.
/// </summary>
/// <remarks>
/// The section is bound by hand (K-021, AOT): a key added to the options and not to
/// the binder is silently ignored. <see cref="OptionsKeyCoverage"/> fails this test
/// before such a key ships unbound (F-247).
/// </remarks>
public sealed class TraconContentGuardOptionsBindingTests
{
    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:ContentGuard:InspectInput"] = "false",
            ["Tracon:ContentGuard:InspectOutput"] = "false",
            ["Tracon:ContentGuard:BufferStreamingOutput"] = "false",
        };

        var options = SectionBinding.Bind<TraconContentGuardOptions>(values);

        options.InspectInput.ShouldBeFalse();
        options.InspectOutput.ShouldBeFalse();
        options.BufferStreamingOutput.ShouldBeFalse();
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:ContentGuard", options);
    }

    [Fact]
    public void An_absent_section_leaves_the_shipped_defaults()
    {
        var options = SectionBinding.Bind<TraconContentGuardOptions>(SectionBinding.NoKeys);

        options.InspectInput.ShouldBeTrue();
        options.InspectOutput.ShouldBeTrue();
        options.BufferStreamingOutput.ShouldBeTrue();
    }
}
