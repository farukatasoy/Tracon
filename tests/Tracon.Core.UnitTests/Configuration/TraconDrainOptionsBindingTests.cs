namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:Drain</c> really reaches <see cref="TraconDrainOptions"/>.
/// </summary>
/// <remarks>
/// The section is bound by hand (AOT): a key added to the options and not to
/// the binder is silently ignored. <see cref="OptionsKeyCoverage"/> fails this test
/// before such a key ships unbound.
/// </remarks>
public sealed class TraconDrainOptionsBindingTests
{
    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Drain:Enabled"] = "true",
            ["Tracon:Drain:Timeout"] = "00:00:45",
        };

        var options = SectionBinding.Bind<TraconDrainOptions>(values);

        options.Enabled.ShouldBeTrue();
        options.Timeout.ShouldBe(TimeSpan.FromSeconds(45));
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:Drain", options);
    }

    [Fact]
    public void An_absent_section_leaves_the_shipped_defaults()
    {
        var options = SectionBinding.Bind<TraconDrainOptions>(SectionBinding.NoKeys);

        options.Enabled.ShouldBeFalse();
        options.Timeout.ShouldBe(TimeSpan.FromSeconds(30));
    }
}
