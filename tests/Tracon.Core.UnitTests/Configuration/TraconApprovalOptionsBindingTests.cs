namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:Approvals</c> really reaches <see cref="TraconApprovalOptions"/>.
/// </summary>
/// <remarks>
/// The section is bound by hand (K-021, AOT): a key added to the options and not to
/// the binder is silently ignored. <see cref="OptionsKeyCoverage"/> fails this test
/// before such a key ships unbound (F-247).
/// </remarks>
public sealed class TraconApprovalOptionsBindingTests
{
    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Approvals:DefaultExpiration"] = "12:00:00",
            ["Tracon:Approvals:ExpirationEnabled"] = "false",
            ["Tracon:Approvals:ScanInterval"] = "00:00:30",
            ["Tracon:Approvals:MaxPerScan"] = "25",
        };

        var options = SectionBinding.Bind<TraconApprovalOptions>(values);

        options.DefaultExpiration.ShouldBe(TimeSpan.FromHours(12));
        options.ExpirationEnabled.ShouldBeFalse();
        options.ScanInterval.ShouldBe(TimeSpan.FromSeconds(30));
        options.MaxPerScan.ShouldBe(25);
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:Approvals", options);
    }

    [Fact]
    public void An_absent_section_leaves_the_shipped_defaults()
    {
        var options = SectionBinding.Bind<TraconApprovalOptions>(SectionBinding.NoKeys);

        options.DefaultExpiration.ShouldBe(TimeSpan.FromHours(24));
        options.ExpirationEnabled.ShouldBeTrue();
        options.ScanInterval.ShouldBe(TimeSpan.FromMinutes(1));
        options.MaxPerScan.ShouldBe(100);
    }
}
