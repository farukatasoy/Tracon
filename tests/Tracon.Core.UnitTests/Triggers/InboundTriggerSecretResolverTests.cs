using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Triggers;

/// <summary>Verifies <see cref="InboundTriggerSecretResolver"/> (section 66.2).</summary>
public sealed class InboundTriggerSecretResolverTests
{
    [Fact]
    public void Name_outside_the_allowed_prefix_is_rejected()
    {
        var exception = Should.Throw<TraconException>(
            () => CreateResolver().ValidateKeyName("default", "ConnectionStrings:Default"));

        exception.Message.ShouldContain("ConnectionStrings:Default");
        exception.Message.ShouldContain("Tracon:TriggerSecrets:");
    }

    [Fact]
    public void Empty_name_is_rejected()
        => Should.Throw<TraconException>(() => CreateResolver().ValidateKeyName("default", ""));

    [Fact]
    public void Name_under_the_prefix_is_accepted()
        => Should.NotThrow(() => CreateResolver().ValidateKeyName("default", "Tracon:TriggerSecrets:Slack"));

    [Fact]
    public void A_custom_prefix_is_honored()
    {
        var resolver = CreateResolver(
            configuration: BuildConfiguration(),
            options: new TraconInboundTriggerOptions { AllowedConfigurationPrefix = "Custom:Prefix:" });

        Should.Throw<TraconException>(() => resolver.ValidateKeyName("default", "Tracon:TriggerSecrets:Slack"));
        Should.NotThrow(() => resolver.ValidateKeyName("default", "Custom:Prefix:Slack"));
    }

    [Fact]
    public void Resolve_returns_the_configured_value()
    {
        var resolver = CreateResolver(BuildConfiguration(("Tracon:TriggerSecrets:Slack", "whsec_test")));

        resolver.Resolve(Trigger("Tracon:TriggerSecrets:Slack")).ShouldBe("whsec_test");
    }

    [Fact]
    public void Resolve_returns_null_when_the_key_has_no_value()
        => CreateResolver().Resolve(Trigger("Tracon:TriggerSecrets:Slack")).ShouldBeNull();

    [Fact]
    public void Resolve_rejects_an_out_of_prefix_trigger_even_if_it_was_saved_before_the_prefix_changed()
    {
        // Defense in two layers (section 66.2): the resolver re-checks the
        // prefix, it does not trust that the save path already validated it.
        var resolver = CreateResolver(BuildConfiguration(("Legacy:Key", "whsec_test")));

        Should.Throw<TraconException>(() => resolver.Resolve(Trigger("Legacy:Key")));
    }

    /// <summary>
    /// A flat name belongs to the default tenant. Another tenant's trigger
    /// that names it would verify signatures with a secret it does not own.
    /// </summary>
    [Theory]
    [InlineData("Tracon:TriggerSecrets:Slack")]
    [InlineData("Tracon:TriggerSecrets:globex:Slack")]
    public void Resolve_rejects_a_trigger_that_names_a_key_outside_its_tenant(string configurationName)
    {
        var resolver = CreateResolver(BuildConfiguration((configurationName, "whsec_other")));

        Should.Throw<TraconException>(
                () => resolver.Resolve(Trigger(configurationName) with { TenantId = "acme" }))
            .Message.ShouldContain("Tracon:TriggerSecrets:acme:");
    }

    [Fact]
    public void Resolve_reads_a_key_under_the_triggers_own_tenant()
    {
        var resolver = CreateResolver(BuildConfiguration(("Tracon:TriggerSecrets:acme:Slack", "whsec_test")));

        resolver.Resolve(Trigger("Tracon:TriggerSecrets:acme:Slack") with { TenantId = "acme" })
            .ShouldBe("whsec_test");
    }

    [Fact]
    public void No_configuration_registered_resolves_to_null()
    {
        var resolver = new InboundTriggerSecretResolver(
            configuration: null,
            new StaticOptionsMonitor<TraconInboundTriggerOptions>(new TraconInboundTriggerOptions()),
            Options.Create(new TraconOptions()));

        resolver.Resolve(Trigger("Tracon:TriggerSecrets:Slack")).ShouldBeNull();
    }

    private static InboundTrigger Trigger(string configurationName)
        => new()
        {
            TenantId = "default",
            Name = "slack",
            TargetKind = InboundTriggerTargetKind.Agent,
            TargetName = "demo",
            SigningSecretConfigurationName = configurationName,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    private static InboundTriggerSecretResolver CreateResolver(
        IConfiguration? configuration = null,
        TraconInboundTriggerOptions? options = null)
        => new(
            configuration ?? BuildConfiguration(),
            new StaticOptionsMonitor<TraconInboundTriggerOptions>(options ?? new TraconInboundTriggerOptions()),
            Options.Create(new TraconOptions()));

    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(static pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();
}
