namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:InboundTriggers</c> really reaches <see cref="TraconInboundTriggerOptions"/>.
/// </summary>
/// <remarks>
/// <para>
/// The section is bound by hand (AOT): a key added to the options and not to the binder is silently ignored. The keys
/// below are built from <see cref="TraconInboundTriggerOptions.SectionName"/>, so the constant, the registration and the
/// binder have to agree; <see cref="OptionsKeyCoverage"/> fails the test before a key ships unbound.
/// </para>
/// <para>
/// <c>AllowedConfigurationPrefix</c> is a security boundary: an unbound key would leave a deployment that wrote it believing
/// trigger secrets can only come from its own prefix.
/// </para>
/// </remarks>
public sealed class TraconInboundTriggerOptionsBindingTests
{
    private const string Section = TraconInboundTriggerOptions.SectionName;

    [Fact]
    public void The_section_name_is_the_documented_path()
        => Section.ShouldBe("Tracon:InboundTriggers");

    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [$"{Section}:TimestampTolerance"] = "00:02:30",
            [$"{Section}:MaxBodyBytes"] = "4096",
            [$"{Section}:MaxRequestsPerMinute"] = "17",
            [$"{Section}:AllowedConfigurationPrefix"] = "Contoso:TriggerSecrets:",
        };

        var options = SectionBinding.Bind<TraconInboundTriggerOptions>(values);

        options.TimestampTolerance.ShouldBe(TimeSpan.FromSeconds(150));
        options.MaxBodyBytes.ShouldBe(4096);
        options.MaxRequestsPerMinute.ShouldBe(17);
        options.AllowedConfigurationPrefix.ShouldBe("Contoso:TriggerSecrets:");
        OptionsKeyCoverage.ShouldCoverEveryMember(values, Section, options);
    }

    [Fact]
    public void An_absent_section_leaves_the_shipped_defaults()
    {
        var options = SectionBinding.Bind<TraconInboundTriggerOptions>(SectionBinding.NoKeys);

        options.TimestampTolerance.ShouldBe(TimeSpan.FromMinutes(5));
        options.MaxBodyBytes.ShouldBe(256 * 1024);
        options.MaxRequestsPerMinute.ShouldBe(60);
        options.AllowedConfigurationPrefix.ShouldBe("Tracon:TriggerSecrets:");
    }

    [Fact]
    public void An_unreadable_value_stops_the_host_instead_of_keeping_the_default()
    {
        // "30s" is not a clock time: the tolerance must not stay five minutes while the operator believes it is 30 seconds.
        var failure = SectionBinding.FailsAtStartup(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [$"{Section}:TimestampTolerance"] = "30s",
        });

        failure.Message.ShouldContain($"{Section}:TimestampTolerance is '30s'");
    }
}
