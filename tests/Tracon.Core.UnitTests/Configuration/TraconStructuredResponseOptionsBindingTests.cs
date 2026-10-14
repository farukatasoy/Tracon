namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:StructuredResponse</c> really reaches <see cref="TraconStructuredResponseOptions"/>.
/// </summary>
/// <remarks>
/// The section is bound by hand (K-021, AOT): a key added to the options and not to
/// the binder is silently ignored. <see cref="OptionsKeyCoverage"/> fails this test
/// before such a key ships unbound (F-247).
/// </remarks>
public sealed class TraconStructuredResponseOptionsBindingTests
{
    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:StructuredResponse:Enabled"] = "true",
            ["Tracon:StructuredResponse:MaxRepairAttempts"] = "2",
        };

        var options = SectionBinding.Bind<TraconStructuredResponseOptions>(values);

        options.Enabled.ShouldBeTrue();
        options.MaxRepairAttempts.ShouldBe(2);
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:StructuredResponse", options);
    }

    [Fact]
    public void An_absent_section_leaves_the_shipped_defaults()
    {
        var options = SectionBinding.Bind<TraconStructuredResponseOptions>(SectionBinding.NoKeys);

        options.Enabled.ShouldBeFalse();
        options.MaxRepairAttempts.ShouldBe(0);
    }
}
