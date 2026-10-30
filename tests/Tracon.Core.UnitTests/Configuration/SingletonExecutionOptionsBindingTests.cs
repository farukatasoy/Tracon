namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:SingletonExecution</c> really reaches <see cref="SingletonExecutionOptions"/>.
/// </summary>
/// <remarks>
/// The section is bound by hand (AOT): a key added to the options and not to
/// the binder is silently ignored. <see cref="OptionsKeyCoverage"/> fails this test
/// before such a key ships unbound.
/// </remarks>
public sealed class SingletonExecutionOptionsBindingTests
{
    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        // While enabled, the lease must be at least SingletonGuard.MinimumLeaseDuration (3 s).
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:SingletonExecution:Enabled"] = "true",
            ["Tracon:SingletonExecution:LeaseDuration"] = "00:00:45",
            ["Tracon:SingletonExecution:OwnerId"] = "node-a",
        };

        var options = SectionBinding.Bind<SingletonExecutionOptions>(values);

        options.Enabled.ShouldBeTrue();
        options.LeaseDuration.ShouldBe(TimeSpan.FromSeconds(45));
        string.Equals(options.OwnerId, "node-a", StringComparison.Ordinal).ShouldBeTrue();
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:SingletonExecution", options);
    }

    [Fact]
    public void An_absent_section_leaves_the_shipped_defaults()
    {
        var options = SectionBinding.Bind<SingletonExecutionOptions>(SectionBinding.NoKeys);

        options.Enabled.ShouldBeFalse();
        options.LeaseDuration.ShouldBe(TimeSpan.FromSeconds(60));
        options.OwnerId.ShouldBeNull();
    }
}
