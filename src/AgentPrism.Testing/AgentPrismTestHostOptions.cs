using AgentPrism;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.Testing;

/// <summary>
/// <see cref="AgentPrismTestHost.StartAsync"/> icin ayarlar.
/// </summary>
public sealed class AgentPrismTestHostOptions
{
    /// <summary>
    /// Host'a kaydedilecek varsayilan model saglayicisi. Kendi agent'lariniz ve
    /// tool'lariniz icin genellikle bunu yapilandirmak yeterlidir.
    /// </summary>
    public FakeModelProvider ModelProvider { get; set; } = new FakeModelProvider().EchoesUserMessage();

    /// <summary>AgentPrism zincirini degistirir (tool, agent, skill, ek model saglayicisi kaydi).</summary>
    public Action<IAgentPrismBuilder>? ConfigureAgentPrism { get; set; }

    /// <summary><c>MapAgentPrism</c> uc ayarlarini degistirir.</summary>
    public Action<AgentPrismEndpointOptions>? ConfigureEndpoints { get; set; }

    /// <summary>Ek servis kaydi yapar. <c>AddAgentPrism()</c> cagrisindan ONCE calisir.</summary>
    public Action<IServiceCollection>? ConfigureServices { get; set; }

    /// <summary>Yol oneki.</summary>
    public string Prefix { get; set; } = AgentPrismEndpointRouteBuilderExtensions.DefaultPrefix;
}
