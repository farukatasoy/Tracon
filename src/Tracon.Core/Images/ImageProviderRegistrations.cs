using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

/// <summary>What the <c>Use*Images</c> calls of the provider packages share: the registration and the settings of one provider.</summary>
internal static class ImageProviderRegistrations
{
    /// <summary>Records that a provider registered an image generator, once.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="providerName">The stable provider name.</param>
    public static void Register(IServiceCollection services, string providerName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        if (services.Any(descriptor =>
                descriptor.ServiceType == typeof(ImageProviderRegistration)
                && descriptor.ImplementationInstance is ImageProviderRegistration existing
                && string.Equals(existing.Name, providerName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        services.AddSingleton(new ImageProviderRegistration(providerName));
    }

    /// <summary>Gets the entry of a provider in the options, creating it when the options have none.</summary>
    /// <param name="options">The image options.</param>
    /// <param name="providerName">The stable provider name.</param>
    /// <returns>The provider's own options.</returns>
    public static TraconImageProviderOptions ProviderOptions(TraconImageOptions options, string providerName)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Providers.TryGetValue(providerName, out var provider))
        {
            provider = new TraconImageProviderOptions();
            options.Providers[providerName] = provider;
        }

        return provider;
    }

    /// <summary>Reads the model set for a provider as it is now; the generator is created with it.</summary>
    /// <param name="services">The service provider.</param>
    /// <param name="providerName">The stable provider name.</param>
    /// <param name="what">What the model is for, as the message names it (<c>OpenAI image generation</c>).</param>
    /// <returns>The model.</returns>
    /// <exception cref="TraconException">No model is set for the provider.</exception>
    public static string RequireModel(IServiceProvider services, string providerName, string what)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = (Microsoft.Extensions.Options.IOptionsMonitor<TraconImageOptions>?)services
            .GetService(typeof(Microsoft.Extensions.Options.IOptionsMonitor<TraconImageOptions>));

        return options?.CurrentValue.Providers.TryGetValue(providerName, out var provider) == true && !string.IsNullOrWhiteSpace(provider.Model)
            ? provider.Model
            : throw new TraconException(
                $"{TraconImageOptions.SectionName}:Providers:{providerName}:Model is required for {what}.");
    }
}
