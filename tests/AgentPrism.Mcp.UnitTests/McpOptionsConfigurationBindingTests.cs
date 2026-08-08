using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPrism.Mcp.UnitTests;

/// <summary>
/// <c>UseMcp(IConfiguration)</c> ayarlari gercekten baglar.
/// </summary>
/// <remarks>
/// 🚨 Bu testin varlik sebebi olculmus bir kusurdur (2026-08-08 raporu, bolum 2.2):
/// <see cref="AgentPrismMcpOptions.SectionName"/> tanimliydi ama hicbir kod
/// <c>Bind</c> cagirmiyordu. <c>AgentPrism:Mcp:RefreshInterval</c> gibi bir ortam
/// degiskeni HICBIR HATA VERMEDEN hicbir sey yapmiyordu. Sessiz yapilandirma
/// kaybi container/K8s dagitimlarinda tipik bir tuzaktir. Gerekce: K-353.
/// </remarks>
public sealed class McpOptionsConfigurationBindingTests
{
    [Fact]
    public void Yapilandirma_bolumu_tum_ayarlari_baglar()
    {
        var options = Resolve(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["AgentPrism:Mcp:Enabled"] = "false",
            ["AgentPrism:Mcp:RefreshInterval"] = "00:10:00",
            ["AgentPrism:Mcp:ConnectionTimeout"] = "00:00:45",
            ["AgentPrism:Mcp:MaxToolsPerServer"] = "7",
            ["AgentPrism:Mcp:MaxResourceBytesPerResource"] = "1024",
            ["AgentPrism:Mcp:MaxResourceBytesTotal"] = "4096",
            ["AgentPrism:Mcp:OAuthCallbackBaseUri"] = "https://myapp.example.com/",
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
    public void Bos_bolum_varsayilanlari_bozmaz()
    {
        var defaults = new AgentPrismMcpOptions();

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
    /// Kodda verilen deger yapilandirmadan gelen degeri EZER. Sira bilincli:
    /// <c>Bind</c> once, <c>configure</c> sonra calisir.
    /// </summary>
    [Fact]
    public void Kod_tarafli_configure_yapilandirmayi_ezer()
    {
        var options = Resolve(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["AgentPrism:Mcp:MaxToolsPerServer"] = "7",
            },
            configure: o => o.MaxToolsPerServer = 42);

        options.MaxToolsPerServer.ShouldBe(42);
    }

    /// <summary>
    /// Ayari GERCEK kayit yolundan cozer: <c>AddAgentPrism().UseMcp(section)</c> →
    /// <c>IOptions&lt;AgentPrismMcpOptions&gt;</c>. Bir prob nesnesi degil, kurulumun
    /// kendisi olculur (bkz. MEMORY.md: "izole olcum entegre davranisi kanitlamaz").
    /// </summary>
    private static AgentPrismMcpOptions Resolve(
        Dictionary<string, string?> settings,
        Action<AgentPrismMcpOptions>? configure = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();

        services.AddAgentPrism()
            .UseMcp(configuration.GetSection(AgentPrismMcpOptions.SectionName), configure);

        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<AgentPrismMcpOptions>>().Value;
    }
}
