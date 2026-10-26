using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:Webhooks</c> really reaches <see cref="TraconWebhookOptions"/>,
/// and a retry delay that cannot be read stops the application at startup.
/// </summary>
/// <remarks>
/// 🚨 F-247: the binder dropped a delay it could not read, and a delay of zero or
/// below, without a word — the retry ladder came out one rung short and the
/// validator's <c>&lt;= TimeSpan.Zero</c> branch could not be reached from
/// configuration. The rule now applies: an unreadable delay is left out and named
/// with its key and its text at startup (F-572), and a delay that is not above zero
/// is the validator's.
/// </remarks>
public sealed class TraconWebhookOptionsBindingTests
{
    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Webhooks:Enabled"] = "false",
            ["Tracon:Webhooks:AllowPrivateNetworkTargets"] = "true",
            ["Tracon:Webhooks:AllowInsecureHttp"] = "true",
            ["Tracon:Webhooks:AllowedConfigurationPrefix"] = "Contoso:WebhookSecrets:",
            ["Tracon:Webhooks:MaxExtraHeaders"] = "5",
            ["Tracon:Webhooks:Timeout"] = "00:00:20",
            ["Tracon:Webhooks:MaxResponseBytes"] = "4096",
            ["Tracon:Webhooks:DisableAfterConsecutiveFailures"] = "3",
            ["Tracon:Webhooks:SignatureTolerance"] = "00:10:00",
        };

        var options = SectionBinding.Bind<TraconWebhookOptions>(values);

        options.Enabled.ShouldBeFalse();
        options.AllowPrivateNetworkTargets.ShouldBeTrue();
        options.AllowInsecureHttp.ShouldBeTrue();
        string.Equals(options.AllowedConfigurationPrefix, "Contoso:WebhookSecrets:", StringComparison.Ordinal).ShouldBeTrue();
        options.MaxExtraHeaders.ShouldBe(5);
        options.Timeout.ShouldBe(TimeSpan.FromSeconds(20));
        options.MaxResponseBytes.ShouldBe(4096);
        options.DisableAfterConsecutiveFailures.ShouldBe(3);
        options.SignatureTolerance.ShouldBe(TimeSpan.FromMinutes(10));
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:Webhooks", options, "RetryDelays");
    }

    [Fact]
    public void The_retry_delays_are_bound()
    {
        var options = SectionBinding.Bind<TraconWebhookOptions>(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Webhooks:RetryDelays:0"] = "00:00:30",
            ["Tracon:Webhooks:RetryDelays:1"] = "00:02:00",
        });

        // A list that is present REPLACES the default five-rung ladder.
        options.RetryDelays.ShouldBe([TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)]);
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:00:05")]
    public void A_retry_delay_that_is_not_positive_fails_at_startup(string value)
    {
        var failure = SectionBinding.FailsAtStartup(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Webhooks:RetryDelays:0"] = "00:01:00",
            ["Tracon:Webhooks:RetryDelays:1"] = value,
        });

        failure.Message.ShouldContain("TraconWebhookOptions.RetryDelays");
    }

    [Fact]
    public void An_unreadable_retry_delay_is_named_as_written()
    {
        // The entry is left out and named with its key and its text: the operator
        // wrote "soon", and the message has to say so.
        var failure = SectionBinding.FailsAtStartup(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Webhooks:RetryDelays:0"] = "00:01:00",
            ["Tracon:Webhooks:RetryDelays:1"] = "soon",
        });

        failure.Message.ShouldContain("Tracon:Webhooks:RetryDelays:1 is 'soon'");
    }

    [Fact]
    public void Code_that_rewrites_the_retry_delays_wins_over_an_unreadable_entry()
    {
        // The failure comes from the bound value (K4, the F-385 precedent).
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Webhooks:RetryDelays:0"] = "soon",
        };

        SectionBinding.StartsCleanly(values, static (builder, _) => builder.Services.Configure<TraconWebhookOptions>(static options =>
        {
            options.RetryDelays.Clear();
            options.RetryDelays.Add(TimeSpan.FromMinutes(1));
        }));
    }

    [Fact]
    public void An_absent_section_leaves_the_shipped_defaults()
    {
        var options = SectionBinding.Bind<TraconWebhookOptions>(SectionBinding.NoKeys);

        options.Enabled.ShouldBeTrue();
        options.AllowPrivateNetworkTargets.ShouldBeFalse();
        options.AllowInsecureHttp.ShouldBeFalse();
        string.Equals(options.AllowedConfigurationPrefix, "Tracon:WebhookSecrets:", StringComparison.Ordinal).ShouldBeTrue();
        options.MaxExtraHeaders.ShouldBe(20);
        options.Timeout.ShouldBe(TimeSpan.FromSeconds(10));
        options.MaxResponseBytes.ShouldBe(8 * 1024);
        options.DisableAfterConsecutiveFailures.ShouldBe(20);
        options.SignatureTolerance.ShouldBe(TimeSpan.FromMinutes(5));
        options.RetryDelays.ShouldBe(
        [
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(2),
            TimeSpan.FromHours(6),
        ]);
    }
}
