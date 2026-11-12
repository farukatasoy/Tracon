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
            .Message.ShouldContain("CanaryOptions.ScanInterval: the bound value '00:05:00' equals the shipped default");
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
    public void A_settable_nested_object_is_checked_member_by_member_not_as_a_scalar()
    {
        // The object is settable, so it used to be taken for a scalar and compared by reference: a bound instance is never
        // the shipped one, and the test passed whatever its members held.
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Probe:Inner:Name"] = "bound",
        };

        var options = new NestedProbeOptions { Inner = new NestedProbeInner { Name = "bound" } };

        Should.Throw<ShouldAssertException>(() => OptionsKeyCoverage.ShouldCoverEveryMember(values, "Probe", options))
            .Message.ShouldContain("NestedProbeOptions.Inner.Limit: no key under 'Probe:Inner:Limit'");
    }

    [Fact]
    public void A_settable_nested_object_the_binder_left_null_fails()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Probe:Inner:Name"] = "bound",
            ["Probe:Inner:Limit"] = "7",
        };

        var options = new NestedProbeOptions { Inner = null! };

        Should.Throw<ShouldAssertException>(() => OptionsKeyCoverage.ShouldCoverEveryMember(values, "Probe", options))
            .Message.ShouldContain("NestedProbeOptions.Inner: a key is named under 'Probe:Inner', but the member stayed null");
    }

    [Fact]
    public void A_value_equal_to_the_default_names_both_causes_not_only_the_test_value()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Probe:Inner:Name"] = "bound",
            ["Probe:Inner:Limit"] = "7",
        };

        // The test value differs from the default (7 against 0), but the binder never read it: the bound member is the default.
        var options = new NestedProbeOptions { Inner = new NestedProbeInner { Name = "bound" } };

        Should.Throw<ShouldAssertException>(() => OptionsKeyCoverage.ShouldCoverEveryMember(values, "Probe", options))
            .Message.ShouldContain("or the binder never read the key");
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

    /// <summary>An options object whose member is a settable nested object.</summary>
    private sealed class NestedProbeOptions
    {
        public NestedProbeInner Inner { get; set; } = new();
    }

    /// <summary>The nested object of <see cref="NestedProbeOptions"/>.</summary>
    private sealed class NestedProbeInner
    {
        public string? Name { get; set; }

        public int Limit { get; set; }
    }
}
