namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:Sessions</c> really reaches <see cref="TraconSessionOptions"/>.
/// </summary>
/// <remarks>
/// The section is bound by hand (AOT): a key added to the options and not to
/// the binder is silently ignored. <see cref="OptionsKeyCoverage"/> fails this test
/// before such a key ships unbound.
/// </remarks>
public sealed class TraconSessionOptionsBindingTests
{
    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Sessions:KeepCanceledTurns"] = "true",
        };

        var options = SectionBinding.Bind<TraconSessionOptions>(values);

        options.KeepCanceledTurns.ShouldBeTrue();
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:Sessions", options);
    }

    [Fact]
    public void An_absent_section_keeps_a_canceled_turn_out_of_the_history()
    {
        SectionBinding.Bind<TraconSessionOptions>(SectionBinding.NoKeys).KeepCanceledTurns.ShouldBeFalse();
    }

    [Fact]
    public void A_text_that_is_not_a_boolean_stops_the_host_instead_of_leaving_the_default()
    {
        var failure = SectionBinding.FailsAtStartup(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Sessions:KeepCanceledTurns"] = "sometimes",
        });

        failure.Message.ShouldContain("KeepCanceledTurns", Case.Insensitive);
    }
}
