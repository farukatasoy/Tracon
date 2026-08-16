using AgentPrism;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.Testing;

/// <summary>
/// Settings for <see cref="AgentPrismTestHost.StartAsync"/>.
/// </summary>
public sealed class AgentPrismTestHostOptions
{
    /// <summary>
    /// The default model provider registered on the host. Usually the only
    /// thing you need to configure for your own agents and tools.
    /// </summary>
    public FakeModelProvider ModelProvider { get; set; } = new FakeModelProvider().EchoesUserMessage();

    /// <summary>Changes the AgentPrism chain (tool, agent, skill, extra model provider registration).</summary>
    public Action<IAgentPrismBuilder>? ConfigureAgentPrism { get; set; }

    /// <summary>Changes the <c>MapAgentPrism</c> endpoint settings.</summary>
    public Action<AgentPrismEndpointOptions>? ConfigureEndpoints { get; set; }

    /// <summary>Registers additional services. Runs BEFORE the <c>AddAgentPrism()</c> call.</summary>
    public Action<IServiceCollection>? ConfigureServices { get; set; }

    /// <summary>Route prefix.</summary>
    public string Prefix { get; set; } = AgentPrismEndpointRouteBuilderExtensions.DefaultPrefix;
}
