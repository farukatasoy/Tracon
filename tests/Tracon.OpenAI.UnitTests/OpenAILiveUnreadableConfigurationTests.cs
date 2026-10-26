using Tracon.OpenAI.UnitTests.Infrastructure;
using Tracon.ProviderCore.Tests;

namespace Tracon.OpenAI.UnitTests;

/// <summary>
/// A value under <c>Tracon:Providers:OpenAI:Live</c> that the binder cannot read stops the host at startup
/// and is named by its full key (phase 247).
/// </summary>
/// <remarks>
/// <para>
/// <c>UseOpenAILive</c> registers its own check: the live options are not the provider options, and
/// <c>UseOpenAI</c> registers the check only for those. Every case goes through the container a host builds
/// (<c>UseOpenAI(key).UseOpenAILive(section)</c>) and <c>IStartupValidator</c>. The probe reflects over
/// <see cref="OpenAILiveOptions"/>, so a scalar added later is covered the day it is added.
/// </para>
/// <para>
/// The probe has no exemption list: the binder reads every scalar of <see cref="OpenAILiveOptions"/>.
/// The properties it skips are text and an address.
/// </para>
/// </remarks>
public sealed class OpenAILiveUnreadableConfigurationTests
{
    private static readonly ProviderConfiguration<OpenAILiveOptions> Provider = new(
        OpenAILiveOptions.SectionName,
        new Dictionary<string, string?>(StringComparer.Ordinal),
        static (builder, section) => builder.UseOpenAI(TestData.ApiKey).UseOpenAILive(section),
        static (builder, configure) => builder.UseOpenAILive(configure));

    [Fact]
    public void Every_scalar_stops_the_host_when_its_text_cannot_be_read()
        => UnreadableProviderConfiguration.EveryScalarStopsTheHost(Provider);

    [Fact]
    public void A_valid_configuration_starts_cleanly()
        => Provider.StartsCleanly([]).Model.ShouldBe(new OpenAILiveOptions().Model);

    [Fact]
    public void A_timeout_of_30s_is_refused_and_named()
    {
        var failure = Provider.FailsAtStartup((nameof(OpenAILiveOptions.Timeout), "30s"));

        failure.Message.ShouldContain(
            "Tracon:Providers:OpenAI:Live:Timeout is '30s', which is not a time span such as 00:00:30. " +
            "Write a valid value, or remove the key to keep the default.");
    }

    [Fact]
    public void An_unreadable_character_limit_is_refused_and_named()
    {
        var failure = Provider.FailsAtStartup((nameof(OpenAILiveOptions.MaxAppendCharacters), "1k"));

        failure.Message.ShouldContain("Tracon:Providers:OpenAI:Live:MaxAppendCharacters is '1k', which is not a 32-bit whole number.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_text_means_not_given_and_the_host_starts(string blank)
        => UnreadableProviderConfiguration.BlankTextKeepsEveryDefault(Provider, blank);

    [Fact]
    public void A_readable_value_with_white_space_around_it_is_read()
    {
        var options = Provider.StartsCleanly(
        [
            (nameof(OpenAILiveOptions.Timeout), " 00:01:30 "),
            (nameof(OpenAILiveOptions.MaxAppendCharacters), " 800 "),
        ]);

        options.Timeout.ShouldBe(TimeSpan.FromSeconds(90));
        options.MaxAppendCharacters.ShouldBe(800);
    }

    [Fact]
    public void A_timeout_assigned_in_the_configure_callback_after_binding_wins_over_an_unreadable_value()
    {
        var options = Provider.StartsCleanly(
            [(nameof(OpenAILiveOptions.Timeout), "30s")],
            static options => options.Timeout = TimeSpan.FromSeconds(45));

        options.Timeout.ShouldBe(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public void Every_scalar_assigned_in_code_after_binding_wins_over_an_unreadable_value()
        => UnreadableProviderConfiguration.CodeAssignedScalarsWinOverUnreadableText(Provider);
}
