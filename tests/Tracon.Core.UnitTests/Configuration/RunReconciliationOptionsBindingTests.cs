namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:RunReconciliation</c> really reaches <see cref="RunReconciliationOptions"/>.
/// </summary>
/// <remarks>
/// The section is bound by hand (K-021, AOT): a key added to the options and not to
/// the binder is silently ignored. <see cref="OptionsKeyCoverage"/> fails this test
/// before such a key ships unbound (F-247).
/// </remarks>
public sealed class RunReconciliationOptionsBindingTests
{
    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        // OrphanThreshold must stay at or above HeartbeatInterval, or the validator stops the container.
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:RunReconciliation:Enabled"] = "true",
            ["Tracon:RunReconciliation:HeartbeatInterval"] = "00:00:10",
            ["Tracon:RunReconciliation:OrphanThreshold"] = "00:02:00",
            ["Tracon:RunReconciliation:ScanInterval"] = "00:00:20",
            ["Tracon:RunReconciliation:MaxRunsPerScan"] = "25",
        };

        var options = SectionBinding.Bind<RunReconciliationOptions>(values);

        options.Enabled.ShouldBeTrue();
        options.HeartbeatInterval.ShouldBe(TimeSpan.FromSeconds(10));
        options.OrphanThreshold.ShouldBe(TimeSpan.FromMinutes(2));
        options.ScanInterval.ShouldBe(TimeSpan.FromSeconds(20));
        options.MaxRunsPerScan.ShouldBe(25);
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:RunReconciliation", options);
    }

    [Fact]
    public void An_absent_section_leaves_the_shipped_defaults()
    {
        var options = SectionBinding.Bind<RunReconciliationOptions>(SectionBinding.NoKeys);

        options.Enabled.ShouldBeFalse();
        options.HeartbeatInterval.ShouldBe(TimeSpan.FromSeconds(30));
        options.OrphanThreshold.ShouldBe(TimeSpan.FromMinutes(5));
        options.ScanInterval.ShouldBe(TimeSpan.FromMinutes(1));
        options.MaxRunsPerScan.ShouldBe(100);
    }
}
