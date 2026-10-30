namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:Idempotency</c> really reaches <see cref="TraconIdempotencyOptions"/>.
/// </summary>
/// <remarks>
/// The section is bound by hand (AOT): a key added to the options and not to
/// the binder is silently ignored. <see cref="OptionsKeyCoverage"/> fails this test
/// before such a key ships unbound.
/// </remarks>
public sealed class TraconIdempotencyOptionsBindingTests
{
    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Idempotency:Enabled"] = "false",
            ["Tracon:Idempotency:MaxKeyLength"] = "128",
        };

        var options = SectionBinding.Bind<TraconIdempotencyOptions>(values);

        options.Enabled.ShouldBeFalse();
        options.MaxKeyLength.ShouldBe(128);
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:Idempotency", options);
    }

    [Fact]
    public void An_absent_section_leaves_the_shipped_defaults()
    {
        var options = SectionBinding.Bind<TraconIdempotencyOptions>(SectionBinding.NoKeys);

        options.Enabled.ShouldBeTrue();
        options.MaxKeyLength.ShouldBe(255);
    }
}
