namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:RateLimit</c> really reaches <see cref="TraconRateLimitOptions"/>.
/// </summary>
/// <remarks>
/// The section is bound by hand (K-021, AOT): a key added to the options and not to
/// the binder is silently ignored. <see cref="OptionsKeyCoverage"/> fails this test
/// before such a key ships unbound (F-247).
/// </remarks>
public sealed class TraconRateLimitOptionsBindingTests
{
    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        // An enum name matches case-insensitively.
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:RateLimit:Enabled"] = "true",
            ["Tracon:RateLimit:PermitLimit"] = "30",
            ["Tracon:RateLimit:Window"] = "00:00:30",
            ["Tracon:RateLimit:QueueLimit"] = "4",
            ["Tracon:RateLimit:Partition"] = "global",
        };

        var options = SectionBinding.Bind<TraconRateLimitOptions>(values);

        options.Enabled.ShouldBeTrue();
        options.PermitLimit.ShouldBe(30);
        options.Window.ShouldBe(TimeSpan.FromSeconds(30));
        options.QueueLimit.ShouldBe(4);
        options.Partition.ShouldBe(RateLimitPartitionKind.Global);
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:RateLimit", options);
    }

    [Fact]
    public void An_absent_section_leaves_the_shipped_defaults()
    {
        var options = SectionBinding.Bind<TraconRateLimitOptions>(SectionBinding.NoKeys);

        options.Enabled.ShouldBeFalse();
        options.PermitLimit.ShouldBe(60);
        options.Window.ShouldBe(TimeSpan.FromMinutes(1));
        options.QueueLimit.ShouldBe(0);
        options.Partition.ShouldBe(RateLimitPartitionKind.Tenant);
    }
}
