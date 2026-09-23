namespace Tracon.Core.UnitTests.Egress;

/// <summary>
/// The key-name rule shared by all four configuration-key surfaces: the
/// prefix (Phase 77) and the tenant's key space inside it.
/// </summary>
public sealed class ConfigurationKeyGuardTests
{
    private const string Prefix = "Tracon:McpSecrets:";
    private const string DefaultTenant = "default";
    private const string Field = "authorizationConfigurationKey";

    private static void Require(string? key, string tenant = DefaultTenant, string prefix = Prefix)
        => ConfigurationKeyGuard.RequireTenantKey(key, prefix, tenant, DefaultTenant, Field);

    // ---- The prefix ---------------------------------------------------------------

    [Fact]
    public void Key_under_the_allowed_prefix_passes()
        => Should.NotThrow(() => Require($"{Prefix}GithubToken"));

    [Fact]
    public void Key_outside_the_allowed_prefix_is_rejected()
    {
        var exception = Should.Throw<TraconException>(() => Require("ConnectionStrings:Default"));

        // The message must name BOTH the field to fix and the prefix to use;
        // that message is the whole upgrade path for an existing record.
        exception.Message.ShouldContain(Field);
        exception.Message.ShouldContain(Prefix);
        exception.Message.ShouldContain("ConnectionStrings:Default");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_key_is_rejected(string? key)
        => Should.Throw<TraconException>(() => Require(key)).Message.ShouldContain(Field);

    /// <summary>🚨 The comparison is ordinal: a case variant must NOT slip through.</summary>
    [Fact]
    public void Prefix_comparison_is_case_sensitive()
        => Should.Throw<TraconException>(() => Require("tracon:mcpsecrets:Token"));

    /// <summary>A key that merely CONTAINS the prefix does not start with it.</summary>
    [Fact]
    public void Prefix_must_be_at_the_start()
        => Should.Throw<TraconException>(() => Require($"Other:{Prefix}Token"));

    /// <summary>An empty allowed prefix would disable the boundary; it is a programming error.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_allowed_prefix_is_a_programming_error(string prefix)
        => Should.Throw<ArgumentException>(() => Require("Tracon:McpSecrets:Token", prefix: prefix));

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

    // ---- The tenant's key space ---------------------------------------------------

    [Theory]
    [InlineData("Tracon:McpSecrets:acme:Token")]
    [InlineData("Tracon:McpSecrets:Acme:Token")]
    [InlineData("Tracon:McpSecrets:acme:Nested:Token")]
    public void Tenant_names_a_key_under_its_own_segment(string key)
        => Should.NotThrow(() => Require(key, tenant: "acme"));

    /// <summary>
    /// 🚨 The prefix is one per installation: without the segment, tenant A
    /// could name tenant B's key and point the record at an address it controls.
    /// </summary>
    [Fact]
    public void Tenant_cannot_name_another_tenants_key()
    {
        var exception = Should.Throw<TraconException>(() => Require("Tracon:McpSecrets:globex:Token", tenant: "acme"));

        exception.Message.ShouldContain(Field);
        exception.Message.ShouldContain("Tracon:McpSecrets:acme:");
    }

    /// <summary>A flat name belongs to the default tenant: another tenant cannot borrow it.</summary>
    [Fact]
    public void Non_default_tenant_cannot_name_a_flat_key()
        => Should.Throw<TraconException>(() => Require("Tracon:McpSecrets:GithubToken", tenant: "acme"));

    [Fact]
    public void Non_default_tenant_cannot_name_the_default_tenants_segment()
        => Should.Throw<TraconException>(() => Require("Tracon:McpSecrets:default:GithubToken", tenant: "acme"));

    /// <summary>A single-tenant installation keeps its flat names unchanged.</summary>
    [Theory]
    [InlineData("Tracon:McpSecrets:GithubToken")]
    [InlineData("Tracon:McpSecrets:default:GithubToken")]
    [InlineData("Tracon:McpSecrets:Default:GithubToken")]
    public void Default_tenant_names_flat_keys_and_its_own_segment(string key)
        => Should.NotThrow(() => Require(key));

    [Fact]
    public void Default_tenant_cannot_name_another_tenants_key()
    {
        var exception = Should.Throw<TraconException>(() => Require("Tracon:McpSecrets:acme:Token"));

        // The default tenant's message names BOTH of its namespaces.
        exception.Message.ShouldContain("Tracon:McpSecrets:default:");
        exception.Message.ShouldContain("flat name");
    }

    /// <summary>A custom default tenant owns the flat names, not the literal "default".</summary>
    [Fact]
    public void The_configured_default_tenant_owns_the_flat_names()
    {
        Should.NotThrow(() => ConfigurationKeyGuard.RequireTenantKey(
            "Tracon:McpSecrets:GithubToken", Prefix, "main", "main", Field));
        Should.Throw<TraconException>(() => ConfigurationKeyGuard.RequireTenantKey(
            "Tracon:McpSecrets:GithubToken", Prefix, "default", "main", Field));
    }

    /// <summary>
    /// A segment must be a whole tenant id: 'acme' must not own 'acme-corp:'.
    /// Tenant ids cannot contain ':', so the separator ends the segment.
    /// </summary>
    [Fact]
    public void Segment_must_match_the_whole_tenant_id()
        => Should.Throw<TraconException>(() => Require("Tracon:McpSecrets:acme-corp:Token", tenant: "acme"));

    [Theory]
    [InlineData("Tracon:McpSecrets:acme:")]
    [InlineData("Tracon:McpSecrets:acme")]
    [InlineData("Tracon:McpSecrets:")]
    public void Segment_without_a_key_name_is_rejected(string key)
        => Should.Throw<TraconException>(() => Require(key, tenant: "acme"));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_tenant_is_a_programming_error(string tenant)
        => Should.Throw<ArgumentException>(() => Require("Tracon:McpSecrets:Token", tenant: tenant));
}
