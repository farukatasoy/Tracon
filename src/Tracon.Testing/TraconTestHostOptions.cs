using Microsoft.Extensions.DependencyInjection;
using Tracon;

namespace Tracon.Testing;

/// <summary>
/// Settings for <see cref="TraconTestHost.StartAsync"/>.
/// </summary>
public sealed class TraconTestHostOptions
{
    /// <summary>
    /// The default model provider registered on the host. Usually the only
    /// thing you need to configure for your own agents and tools.
    /// </summary>
    public FakeModelProvider ModelProvider { get; set; } = new FakeModelProvider().EchoesUserMessage();

    /// <summary>Changes the Tracon chain (tool, agent, skill, extra model provider registration).</summary>
    public Action<ITraconBuilder>? ConfigureTracon { get; set; }

    /// <summary>Changes the <c>MapTracon</c> endpoint settings.</summary>
    public Action<TraconEndpointOptions>? ConfigureEndpoints { get; set; }

    /// <summary>Registers additional services. Runs BEFORE the <c>AddTracon()</c> call.</summary>
    public Action<IServiceCollection>? ConfigureServices { get; set; }

    /// <summary>Route prefix.</summary>
    public string Prefix { get; set; } = TraconEndpointRouteBuilderExtensions.DefaultPrefix;
}
