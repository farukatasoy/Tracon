namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:RunContinuation</c> really reaches <see cref="TraconRunContinuationOptions"/>.
/// </summary>
/// <remarks>
/// The section is bound by hand (AOT): a key added to the options and not to
/// the binder is silently ignored. <see cref="OptionsKeyCoverage"/> fails this test
/// before such a key ships unbound.
/// </remarks>
public sealed class TraconRunContinuationOptionsBindingTests
{
    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:RunContinuation:Enabled"] = "true",
            ["Tracon:RunContinuation:MaxAttempts"] = "3",
        };

        var options = SectionBinding.Bind<TraconRunContinuationOptions>(values);

        options.Enabled.ShouldBeTrue();
        options.MaxAttempts.ShouldBe(3);
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:RunContinuation", options);
    }

    [Fact]
    public void An_absent_section_leaves_the_shipped_defaults()
    {
        var options = SectionBinding.Bind<TraconRunContinuationOptions>(SectionBinding.NoKeys);

        options.Enabled.ShouldBeFalse();
        options.MaxAttempts.ShouldBe(1);
    }
}
