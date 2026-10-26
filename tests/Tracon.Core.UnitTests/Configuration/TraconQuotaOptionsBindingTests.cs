using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:Quotas</c> really reaches <see cref="TraconQuotaOptions"/>,
/// and a threshold that cannot be read stops the application at startup.
/// </summary>
/// <remarks>
/// 🚨 F-247: the binder dropped a threshold it could not read, and a threshold of
/// zero or below, without a word. <c>["80", "yuz"]</c> started with <c>[80]</c>,
/// and the validator's <c>&lt;= 0</c> branch could not be reached from configuration
/// at all. The rule now applies: an unreadable entry is left out and named with its
/// key and its text at startup (F-572), and an entry out of range is the validator's.
/// </remarks>
public sealed class TraconQuotaOptionsBindingTests
{
    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Quotas:Enabled"] = "false",
            ["Tracon:Quotas:TimeZone"] = "Europe/Istanbul",
            ["Tracon:Quotas:AllowOnStoreFailure"] = "false",
            ["Tracon:Quotas:PublishThresholdToRunStream"] = "true",
        };

        var options = SectionBinding.Bind<TraconQuotaOptions>(values);

        options.Enabled.ShouldBeFalse();
        string.Equals(options.TimeZone, "Europe/Istanbul", StringComparison.Ordinal).ShouldBeTrue();
        options.AllowOnStoreFailure.ShouldBeFalse();
        options.PublishThresholdToRunStream.ShouldBeTrue();
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:Quotas", options, "ThresholdPercents");
    }

    [Fact]
    public void The_threshold_percents_are_bound()
    {
        var options = SectionBinding.Bind<TraconQuotaOptions>(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Quotas:ThresholdPercents:0"] = "50",
            ["Tracon:Quotas:ThresholdPercents:1"] = "90",
        });

        // A list that is present REPLACES the default [80, 100].
        options.ThresholdPercents.ShouldBe([50, 90]);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void A_threshold_outside_the_range_fails_at_startup(string value)
    {
        var failure = SectionBinding.FailsAtStartup(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Quotas:ThresholdPercents:0"] = "80",
            ["Tracon:Quotas:ThresholdPercents:1"] = value,
        });

        failure.Message.ShouldContain("TraconQuotaOptions.ThresholdPercents");
    }

    [Fact]
    public void An_unreadable_threshold_is_named_as_written()
    {
        // The entry is left out and named with its key and its text: the operator
        // wrote "9O", and the list alone would not show where.
        var failure = SectionBinding.FailsAtStartup(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Quotas:ThresholdPercents:0"] = "80",
            ["Tracon:Quotas:ThresholdPercents:1"] = "9O",
        });

        failure.Message.ShouldContain("Tracon:Quotas:ThresholdPercents:1 is '9O'");
    }

    [Fact]
    public void Code_that_rewrites_the_thresholds_wins_over_an_unreadable_entry()
    {
        // The failure comes from the bound value, not from a record of the raw
        // text: code that configures the same member after binding replaces the
        // unreadable entry, and the application starts (K4, the F-385 precedent).
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Quotas:ThresholdPercents:0"] = "yuz",
        };

        SectionBinding.StartsCleanly(values, static (builder, _) => builder.Services.Configure<TraconQuotaOptions>(static options =>
        {
            options.ThresholdPercents.Clear();
            options.ThresholdPercents.Add(75);
        }));
    }

    [Fact]
    public void An_absent_section_leaves_the_shipped_defaults()
    {
        var options = SectionBinding.Bind<TraconQuotaOptions>(SectionBinding.NoKeys);

        options.Enabled.ShouldBeTrue();
        string.Equals(options.TimeZone, "UTC", StringComparison.Ordinal).ShouldBeTrue();
        options.ThresholdPercents.ShouldBe([80, 100]);
        options.AllowOnStoreFailure.ShouldBeTrue();
        options.PublishThresholdToRunStream.ShouldBeFalse();
    }
}
