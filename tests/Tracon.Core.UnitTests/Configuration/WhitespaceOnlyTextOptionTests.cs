using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// A text option made only of white space stops the host, from configuration and from code alike.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ConfigurationPermutationTests"/> walks every text option of a type that carries a section name and writes
/// blanks into it. Two options types are bound under a section the permutation cannot find by reflection (they have no
/// <c>SectionName</c> constant), and a value assigned in code never passes a binder at all; both are tried here by name.
/// </para>
/// <para>
/// Empty is a different value: a key with no text is not given and the option keeps its default.
/// </para>
/// </remarks>
public sealed class WhitespaceOnlyTextOptionTests
{
    [Theory]
    [InlineData("Tracon:TenantProviders:AllowedConfigurationPrefix", "TraconTenantProviderOptions.AllowedConfigurationPrefix")]
    [InlineData("Tracon:InboundTriggers:AllowedConfigurationPrefix", "TraconInboundTriggerOptions.AllowedConfigurationPrefix")]
    public void A_prefix_of_blanks_on_an_options_type_without_a_section_constant_stops_the_host(string key, string member)
    {
        var failure = SectionBinding.FailsAtStartup(
            new Dictionary<string, string?>(StringComparer.Ordinal) { [key] = "   " });

        failure.Message.ShouldContain(member, Case.Sensitive);
        failure.Message.ShouldContain("white space", Case.Sensitive);
    }

    [Fact]
    public void A_value_assigned_in_code_is_refused_by_the_same_validator()
    {
        var services = new ServiceCollection();
        services.AddTracon();
        services.Configure<TraconOptions>(static options => options.Audit.ChainKeyId = "\t ");

        using var provider = services.BuildServiceProvider();

        var failure = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        failure.Message.ShouldContain("Audit.ChainKeyId", Case.Sensitive);
    }

    [Fact]
    public void Empty_text_is_not_given_and_the_host_starts()
    {
        SectionBinding.StartsCleanly(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Tracon:Audit:ChainKeyId"] = string.Empty,
                ["Tracon:TenantProviders:AllowedConfigurationPrefix"] = string.Empty,
                ["Tracon:SessionOwnership:ManagementPolicy"] = string.Empty,
            });
    }

    [Fact]
    public void A_value_with_a_visible_character_is_kept_as_written()
    {
        var options = SectionBinding.Bind<TraconWebhookOptions>(
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["Tracon:Webhooks:AllowedConfigurationPrefix"] = " Tracon:Hooks: " });

        options.AllowedConfigurationPrefix.ShouldBe(" Tracon:Hooks: ");
    }
}
