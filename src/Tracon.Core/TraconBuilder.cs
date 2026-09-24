using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

/// <summary>The default implementation of <see cref="ITraconBuilder"/>.</summary>
/// <remarks>
/// Carries the service collection and nothing else; every registration
/// method is an extension in <see cref="TraconBuilderExtensions"/>.
/// </remarks>
internal sealed class TraconBuilder : ITraconBuilder
{
    public TraconBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    public IServiceCollection Services { get; }
}
