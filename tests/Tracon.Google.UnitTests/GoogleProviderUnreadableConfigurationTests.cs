using Tracon.Google.UnitTests.Infrastructure;
using Tracon.ProviderCore.Tests;

namespace Tracon.Google.UnitTests;

/// <summary>
/// A value under <c>Tracon:Providers:Google</c> that the binder cannot read stops the host at startup
/// and is named by its full key.
/// </summary>
/// <remarks>
/// <para>
/// Before the phase the binder skipped such a value and kept the shipped default:
/// <c>Timeout = "30s"</c> kept the default timeout without a word. Every case goes through the container a
/// host builds and <c>IStartupValidator</c>. The probes in <see cref="UnreadableProviderConfiguration"/>
/// reflect over <see cref="GoogleProviderOptions"/> and <see cref="ModelDescriptor"/>, so a scalar added
/// later is covered the day it is added.
/// </para>
/// <para>
/// The probe has no exemption list: the binder reads every scalar of <see cref="GoogleProviderOptions"/>.
/// The properties it skips are text, an address and the catalog list, which the scalar readers do not cover.
/// </para>
/// </remarks>
public sealed class GoogleProviderUnreadableConfigurationTests
{
    private static readonly ProviderConfiguration<GoogleProviderOptions> Provider = new(
        GoogleProviderOptions.SectionName,
        new Dictionary<string, string?>(StringComparer.Ordinal) { [nameof(GoogleProviderOptions.ApiKey)] = TestData.ApiKey },
        static (builder, section) => builder.UseGoogle(section),
        static (builder, configure) => builder.UseGoogle(configure));

    [Fact]
    public void Every_scalar_stops_the_host_when_its_text_cannot_be_read()
        => UnreadableProviderConfiguration.EveryScalarStopsTheHost(Provider);

    [Fact]
    public void Every_scalar_of_a_catalog_entry_stops_the_host_when_its_text_cannot_be_read()
        => UnreadableProviderConfiguration.EveryCatalogScalarStopsTheHost(Provider);

    [Fact]
    public void A_valid_configuration_starts_cleanly()
        => Provider.StartsCleanly([]).ApiKey.ShouldBe(TestData.ApiKey);

    [Fact]
    public void A_timeout_of_30s_is_refused_and_named()
    {
        var failure = Provider.FailsAtStartup((nameof(GoogleProviderOptions.Timeout), "30s"));

        failure.Message.ShouldContain(
            "Tracon:Providers:Google:Timeout is '30s', which is not a time span such as 00:00:30. " +
            "Write a valid value, or remove the key to keep the default.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_text_means_not_given_and_the_host_starts(string blank)
        => UnreadableProviderConfiguration.BlankTextKeepsEveryDefault(Provider, blank);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_catalog_text_means_not_given_and_the_host_starts(string blank)
        => UnreadableProviderConfiguration.BlankTextKeepsEveryCatalogDefault(Provider, blank, static options => options.Models);

    [Fact]
    public void A_readable_value_with_white_space_around_it_is_read()
        => Provider.StartsCleanly([(nameof(GoogleProviderOptions.Timeout), " 00:01:30 ")])
            .Timeout.ShouldBe(TimeSpan.FromSeconds(90));

    [Fact]
    public void A_timeout_assigned_in_the_configure_callback_after_binding_wins_over_an_unreadable_value()
    {
        var options = Provider.StartsCleanly(
            [(nameof(GoogleProviderOptions.Timeout), "30s")],
            static options => options.Timeout = TimeSpan.FromSeconds(45));

        options.Timeout.ShouldBe(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public void Every_scalar_assigned_in_code_after_binding_wins_over_an_unreadable_value()
        => UnreadableProviderConfiguration.CodeAssignedScalarsWinOverUnreadableText(Provider);

    [Fact]
    public void A_catalog_field_that_cannot_be_read_is_named_by_its_full_key()
        => UnreadableProviderConfiguration.EveryUnreadableCatalogFieldIsNamedByItsFullKey(Provider);

    [Fact]
    public void A_valid_catalog_still_binds()
        => UnreadableProviderConfiguration.AValidCatalogEntryBindsEveryField(Provider, static options => options.Models);

    [Fact]
    public void A_model_removed_in_code_after_binding_no_longer_stops_the_host()
    {
        var options = Provider.StartsCleanly(
            [("Models:0:Name", "model-a"), ("Models:0:ContextWindowTokens", "128k")],
            static options => options.Models.Clear());

        options.Models.ShouldBeEmpty();
    }
}
