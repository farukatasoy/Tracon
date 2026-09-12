namespace Tracon.Core.UnitTests.Egress;

/// <summary>
/// The prefix rule shared by all four configuration-key surfaces (Phase 77).
/// </summary>
public sealed class ConfigurationKeyGuardTests
{
    private const string Prefix = "Tracon:McpSecrets:";

    [Fact]
    public void Key_under_the_allowed_prefix_passes()
        => Should.NotThrow(() =>
            ConfigurationKeyGuard.RequirePrefix($"{Prefix}GithubToken", Prefix, "authorizationConfigurationKey"));

    [Fact]
    public void Key_outside_the_allowed_prefix_is_rejected()
    {
        var exception = Should.Throw<TraconException>(() =>
            ConfigurationKeyGuard.RequirePrefix("ConnectionStrings:Default", Prefix, "authorizationConfigurationKey"));

        // The message must name BOTH the field to fix and the prefix to use;
        // that message is the whole upgrade path for an existing record.
        exception.Message.ShouldContain("authorizationConfigurationKey");
        exception.Message.ShouldContain(Prefix);
        exception.Message.ShouldContain("ConnectionStrings:Default");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_key_is_rejected(string? key)
        => Should.Throw<TraconException>(() =>
            ConfigurationKeyGuard.RequirePrefix(key, Prefix, "authorizationConfigurationKey"))
            .Message.ShouldContain("authorizationConfigurationKey");

    /// <summary>🚨 The comparison is ordinal: a case variant must NOT slip through.</summary>
    [Fact]
    public void Prefix_comparison_is_case_sensitive()
        => Should.Throw<TraconException>(() =>
            ConfigurationKeyGuard.RequirePrefix("tracon:mcpsecrets:Token", Prefix, "authorizationConfigurationKey"));

    /// <summary>A key that merely CONTAINS the prefix does not start with it.</summary>
    [Fact]
    public void Prefix_must_be_at_the_start()
        => Should.Throw<TraconException>(() =>
            ConfigurationKeyGuard.RequirePrefix($"Other:{Prefix}Token", Prefix, "authorizationConfigurationKey"));

    /// <summary>An empty allowed prefix would disable the boundary; it is a programming error.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_allowed_prefix_is_a_programming_error(string prefix)
        => Should.Throw<ArgumentException>(() =>
            ConfigurationKeyGuard.RequirePrefix("Tracon:McpSecrets:Token", prefix, "field"));

    /// <summary>The four shipped defaults must stay distinct and end with a separator.</summary>
    [Fact]
    public void Shipped_default_prefixes_are_distinct_and_terminated()
    {
        var defaults = new[]
        {
            new TraconInboundTriggerOptions().AllowedConfigurationPrefix,
            new TraconTenantProviderOptions().AllowedConfigurationPrefix,
            new TraconMcpSecurityOptions().AllowedConfigurationPrefix,
            new TraconWebhookOptions().AllowedConfigurationPrefix,
        };

        defaults.Distinct(StringComparer.Ordinal).Count().ShouldBe(defaults.Length);

        foreach (var prefix in defaults)
        {
            // Without the trailing separator, "Tracon:McpSecrets" would also
            // match "Tracon:McpSecretsSomethingElse:Key".
            prefix.ShouldEndWith(":");
            prefix.ShouldStartWith("Tracon:");
        }
    }
}
