namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:AsyncRun</c> really reaches <see cref="TraconAsyncRunOptions"/>.
/// </summary>
/// <remarks>
/// The section is bound by hand (AOT): a key added to the options and not to
/// the binder is silently ignored. <see cref="OptionsKeyCoverage"/> fails this test
/// before such a key ships unbound.
/// </remarks>
public sealed class TraconAsyncRunOptionsBindingTests
{
    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:AsyncRun:Enabled"] = "false",
            ["Tracon:AsyncRun:MaxAttempts"] = "3",
        };

        var options = SectionBinding.Bind<TraconAsyncRunOptions>(values);

        options.Enabled.ShouldBeFalse();
        options.MaxAttempts.ShouldBe(3);
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:AsyncRun", options);
    }

    [Fact]
    public void An_absent_section_leaves_the_shipped_defaults()
    {
        var options = SectionBinding.Bind<TraconAsyncRunOptions>(SectionBinding.NoKeys);

        options.Enabled.ShouldBeTrue();
        options.MaxAttempts.ShouldBe(1);
    }
}
