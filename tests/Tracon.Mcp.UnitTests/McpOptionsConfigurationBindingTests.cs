using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// <c>UseMcp(IConfiguration)</c> actually binds the settings.
/// </summary>
/// <remarks>
/// 🚨 This test exists because of a measured defect (2026-08-08 report, section 2.2):
/// <see cref="TraconMcpOptions.SectionName"/> was defined, but no code
/// called <c>Bind</c>. An environment variable such as <c>Tracon:Mcp:RefreshInterval</c>
/// did NOTHING, WITHOUT ANY ERROR. Silent configuration loss is a typical trap
/// in container/K8s deployments. Rationale: K-353.
/// </remarks>
public sealed class McpOptionsConfigurationBindingTests
{
    [Fact]
    public void Configuration_section_binds_all_settings()
    {
        var options = Resolve(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Mcp:Enabled"] = "false",
            ["Tracon:Mcp:RefreshInterval"] = "00:10:00",
            ["Tracon:Mcp:ConnectionTimeout"] = "00:00:45",
            ["Tracon:Mcp:MaxToolsPerServer"] = "7",
            ["Tracon:Mcp:MaxResourceBytesPerResource"] = "1024",
            ["Tracon:Mcp:MaxResourceBytesTotal"] = "4096",
            ["Tracon:Mcp:OAuthCallbackBaseUri"] = "https://myapp.example.com/",
        });

        options.Enabled.ShouldBeFalse();
        options.RefreshInterval.ShouldBe(TimeSpan.FromMinutes(10));
        options.ConnectionTimeout.ShouldBe(TimeSpan.FromSeconds(45));
        options.MaxToolsPerServer.ShouldBe(7);
        options.MaxResourceBytesPerResource.ShouldBe(1024);
        options.MaxResourceBytesTotal.ShouldBe(4096);
        options.OAuthCallbackBaseUri.ShouldBe(new Uri("https://myapp.example.com/"));
    }

    [Fact]
    public void Empty_section_does_not_break_defaults()
    {
        var defaults = new TraconMcpOptions();

        var options = Resolve([]);

        options.Enabled.ShouldBe(defaults.Enabled);
        options.RefreshInterval.ShouldBe(defaults.RefreshInterval);
        options.ConnectionTimeout.ShouldBe(defaults.ConnectionTimeout);
        options.MaxToolsPerServer.ShouldBe(defaults.MaxToolsPerServer);
        options.MaxResourceBytesPerResource.ShouldBe(defaults.MaxResourceBytesPerResource);
        options.MaxResourceBytesTotal.ShouldBe(defaults.MaxResourceBytesTotal);
        options.OAuthCallbackBaseUri.ShouldBeNull();
    }

    /// <summary>
    /// A value given in code OVERWRITES the value coming from configuration. The order
    /// is deliberate: <c>Bind</c> runs first, <c>configure</c> runs after.
    /// </summary>
    [Fact]
    public void Code_side_configure_overwrites_configuration()
    {
        var options = Resolve(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Tracon:Mcp:MaxToolsPerServer"] = "7",
            },
            configure: o => o.MaxToolsPerServer = 42);

        options.MaxToolsPerServer.ShouldBe(42);
    }

    /// <summary>
    /// Resolves the setting through the REAL registration path: <c>AddTracon().UseMcp(section)</c> →
    /// <c>IOptions&lt;TraconMcpOptions&gt;</c>. It is the setup itself that is measured, not a
    /// probe object (see MEMORY.md: "an isolated measurement does not prove integrated behavior").
    /// </summary>
    private static TraconMcpOptions Resolve(
        Dictionary<string, string?> settings,
        Action<TraconMcpOptions>? configure = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();

        services.AddTracon()
            .UseMcp(configuration.GetSection(TraconMcpOptions.SectionName), configure);

        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<TraconMcpOptions>>().Value;
    }
}
