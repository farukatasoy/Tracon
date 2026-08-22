using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Resolves the configured image provider without conflating it with another provider.</summary>
/// <remarks>
/// Provider packages register keyed generators using their stable provider names.
/// The unkeyed fallback preserves the public MEAI extension point for a consumer
/// that registers one custom generator directly.
/// </remarks>
#pragma warning disable MEAI001
internal sealed class ImageGeneratorResolver
{
    private readonly IServiceProvider _services;
    private readonly IOptions<AgentPrismImageOptions> _options;

    public ImageGeneratorResolver(IServiceProvider services, IOptions<AgentPrismImageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        _services = services;
        _options = options;
    }

    public IImageGenerator Resolve()
    {
        var providerName = _options.Value.Provider;
        var generator = _services.GetKeyedService<IImageGenerator>(providerName)
            ?? _services.GetService<IImageGenerator>();

        return generator ?? throw new AgentPrismException(
            $"{nameof(AgentPrismImageOptions)} is enabled but no image generator is registered for provider " +
            $"'{providerName}'. Install a supported provider package and call UseOpenAIImages(), " +
            "UseAzureOpenAIImages(), or UseGoogleImages().");
    }
}
#pragma warning restore MEAI001
