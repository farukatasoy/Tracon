namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:Mcp</c> really reaches <see cref="TraconMcpSecurityOptions"/>.
/// </summary>
/// <remarks>
/// <para>
/// The section is bound by hand (AOT): a key added to the options and not to
/// the binder is silently ignored. <see cref="OptionsKeyCoverage"/> fails this test
/// before such a key ships unbound.
/// </para>
/// <para>
/// The section is shared with <c>TraconMcpOptions</c>; this class reads only its prefix.
/// </para>
/// </remarks>
public sealed class TraconMcpSecurityOptionsBindingTests
{
    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Mcp:AllowedConfigurationPrefix"] = "Contoso:McpSecrets:",
        };

        var options = SectionBinding.Bind<TraconMcpSecurityOptions>(values);

        string.Equals(options.AllowedConfigurationPrefix, "Contoso:McpSecrets:", StringComparison.Ordinal).ShouldBeTrue();
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:Mcp", options);
    }

    [Fact]
    public void An_absent_section_leaves_the_shipped_defaults()
    {
        var options = SectionBinding.Bind<TraconMcpSecurityOptions>(SectionBinding.NoKeys);

        string.Equals(options.AllowedConfigurationPrefix, "Tracon:McpSecrets:", StringComparison.Ordinal).ShouldBeTrue();
    }
}
