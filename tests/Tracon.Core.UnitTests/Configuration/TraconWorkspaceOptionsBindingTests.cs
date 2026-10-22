namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:Workspace</c> really reaches <see cref="TraconWorkspaceOptions"/>.
/// </summary>
/// <remarks>
/// The section is bound by hand (K-021, AOT): a key added to the options and not to
/// the binder is silently ignored. <see cref="OptionsKeyCoverage"/> fails this test
/// before such a key ships unbound (F-247).
/// </remarks>
public sealed class TraconWorkspaceOptionsBindingTests
{
    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Workspace:MaxFileBytes"] = "1000",
            ["Tracon:Workspace:MaxSessionBytes"] = "2000",
            ["Tracon:Workspace:MaxTenantBytes"] = "30000000000",
        };

        var options = SectionBinding.Bind<TraconWorkspaceOptions>(values);

        options.MaxFileBytes.ShouldBe(1000);
        options.MaxSessionBytes.ShouldBe(2000);
        options.MaxTenantBytes.ShouldBe(30_000_000_000, "a limit past int.MaxValue is read as a long");
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:Workspace", options);
    }

    [Fact]
    public void An_absent_section_leaves_the_shipped_defaults()
    {
        var options = SectionBinding.Bind<TraconWorkspaceOptions>(SectionBinding.NoKeys);

        options.MaxFileBytes.ShouldBe(256 * 1024);
        options.MaxSessionBytes.ShouldBe(5 * 1024 * 1024);
        options.MaxTenantBytes.ShouldBe(500L * 1024 * 1024);
    }
}
