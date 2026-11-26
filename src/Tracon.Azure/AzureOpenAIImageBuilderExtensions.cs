using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Adds Azure OpenAI image generation to the Tracon chain.</summary>
public static class AzureOpenAIImageBuilderExtensions
{
    /// <summary>Registers the Azure OpenAI image generator.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="configure">Optional settings of this provider: its deployment and its provider settings.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <remarks>
    /// Call <see cref="AzureOpenAIProviderExtensions.UseAzureOpenAI(ITraconBuilder, Uri, string, Action{AzureOpenAIProviderOptions}?)"/>
    /// first. The image model is an Azure deployment name, not a public model id. <paramref name="configure"/> changes the entry
    /// of <c>azure-openai</c> in <see cref="TraconImageOptions.Providers"/> and no other; <c>azure-openai.quality</c> in
    /// <see cref="TraconImageProviderOptions.ProviderSettings"/> sets the image quality.
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseAzureOpenAI(
    ///            builder.Configuration.GetSection(AzureOpenAIProviderOptions.SectionName))
    ///        .UseAzureOpenAIImages(provider =&gt; provider.Model = "image-deployment")
    ///        .ConfigureImages(images =&gt; images.Enabled = true);
    /// </code>
    /// </example>
    /// </remarks>
#pragma warning disable MEAI001
    public static ITraconBuilder UseAzureOpenAIImages(
        this ITraconBuilder builder,
        Action<TraconImageProviderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        ImageProviderRegistrations.Register(services, AzureOpenAIProviderNames.AzureOpenAI);

        if (configure is not null)
        {
            services.Configure<TraconImageOptions>(
                options => configure(ImageProviderRegistrations.ProviderOptions(options, AzureOpenAIProviderNames.AzureOpenAI)));
        }

        services.TryAddKeyedSingleton<IImageGenerator>(AzureOpenAIProviderNames.AzureOpenAI, static (provider, _) =>
        {
            var deploymentName = ImageProviderRegistrations.RequireModel(
                provider,
                AzureOpenAIProviderNames.AzureOpenAI,
                "Azure OpenAI image generation");

            return provider.GetRequiredService<AzureOpenAISetup>().CreateImageGenerator(deploymentName);
        });

        return builder;
    }
#pragma warning restore MEAI001
}
