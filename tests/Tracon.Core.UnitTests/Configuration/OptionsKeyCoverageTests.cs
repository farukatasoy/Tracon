namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// <see cref="OptionsKeyCoverage"/> fails for each gap it exists to close. A helper
/// that passed everything would make all 22 binding tests empty proof.
/// </summary>
public sealed class OptionsKeyCoverageTests
{
    [Fact]
    public void A_member_no_key_names_fails()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Canary:AutoRollbackEnabled"] = "true",
        };

        var options = new CanaryOptions { AutoRollbackEnabled = true };

        Should.Throw<ShouldAssertException>(() => OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:Canary", options))
            .Message.ShouldContain("CanaryOptions.ScanInterval: no key under 'Tracon:Canary:ScanInterval'");
    }

    [Fact]
    public void A_value_equal_to_the_shipped_default_fails()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Canary:AutoRollbackEnabled"] = "true",
            ["Tracon:Canary:ScanInterval"] = "00:05:00",
        };

        var options = new CanaryOptions { AutoRollbackEnabled = true, ScanInterval = TimeSpan.FromMinutes(5) };

        Should.Throw<ShouldAssertException>(() => OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:Canary", options))
            .Message.ShouldContain("CanaryOptions.ScanInterval: the test value '00:05:00' equals the shipped default");
    }

    [Fact]
    public void A_collection_equal_to_the_shipped_default_fails()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Quotas:Enabled"] = "false",
            ["Tracon:Quotas:TimeZone"] = "Europe/Istanbul",
            ["Tracon:Quotas:AllowOnStoreFailure"] = "false",
            ["Tracon:Quotas:PublishThresholdToRunStream"] = "true",
            ["Tracon:Quotas:ThresholdPercents:0"] = "80",
            ["Tracon:Quotas:ThresholdPercents:1"] = "100",
        };

        var options = new TraconQuotaOptions
        {
            Enabled = false,
            TimeZone = "Europe/Istanbul",
            AllowOnStoreFailure = false,
            PublishThresholdToRunStream = true,
        };

        Should.Throw<ShouldAssertException>(() => OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:Quotas", options))
            .Message.ShouldContain("TraconQuotaOptions.ThresholdPercents: the bound collection equals the shipped default");
    }

    [Fact]
    public void A_nested_member_no_key_names_fails()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Retention:RunEvents:MaxAgeDays"] = "3",
        };

        var options = new TraconRetentionOptions();
        options.RunEvents.MaxAgeDays = 3;

        Should.Throw<ShouldAssertException>(() => OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:Retention", options))
            .Message.ShouldContain("TraconRetentionOptions.RunEvents.Archive: no key under 'Tracon:Retention:RunEvents:Archive'");
    }

    [Fact]
    public void An_exemption_for_a_member_that_does_not_exist_fails()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Canary:AutoRollbackEnabled"] = "true",
            ["Tracon:Canary:ScanInterval"] = "00:00:45",
        };

        var options = new CanaryOptions { AutoRollbackEnabled = true, ScanInterval = TimeSpan.FromSeconds(45) };

        Should.Throw<ShouldAssertException>(() => OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:Canary", options, "Lanes"))
            .Message.ShouldContain("'Lanes' is exempt, but CanaryOptions has no such member.");
    }
}
