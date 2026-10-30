namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:Canary</c> really reaches <see cref="CanaryOptions"/>.
/// </summary>
/// <remarks>
/// The section is bound by hand (AOT). A key added to the options and not to
/// the binder is silently ignored; <see cref="OptionsKeyCoverage"/> fails such a test
/// before the key ships unbound.
/// </remarks>
public sealed class CanaryOptionsBindingTests
{
    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Canary:AutoRollbackEnabled"] = "true",
            ["Tracon:Canary:ScanInterval"] = "00:00:45",
        };

        var options = SectionBinding.Bind<CanaryOptions>(values);

        options.AutoRollbackEnabled.ShouldBeTrue();
        options.ScanInterval.ShouldBe(TimeSpan.FromSeconds(45));
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:Canary", options);
    }

    [Fact]
    public void An_absent_section_leaves_the_shipped_defaults()
    {
        var options = SectionBinding.Bind<CanaryOptions>(SectionBinding.NoKeys);

        options.AutoRollbackEnabled.ShouldBeFalse();
        options.ScanInterval.ShouldBe(TimeSpan.FromMinutes(5));
    }
}
