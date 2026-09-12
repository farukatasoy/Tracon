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
    /// <param name="configure">Optional image-tool settings.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <remarks>
    /// Call <see cref="AzureOpenAIProviderExtensions.UseAzureOpenAI(ITraconBuilder, Uri, string, Action{AzureOpenAIProviderOptions}?)"/>
    /// first. The image model is an Azure deployment name, not a public model id.
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseAzureOpenAI(
    ///            new Uri(builder.Configuration["AzureOpenAI:Endpoint"]!),
    ///            builder.Configuration["AzureOpenAI:ApiKey"]!)
    ///        .UseAzureOpenAIImages(options =&gt;
    ///        {
    ///            options.Enabled = true;
    ///            options.Model = "image-deployment";
    ///        });
    /// </code>
    /// </example>
    /// </remarks>
#pragma warning disable MEAI001
    public static ITraconBuilder UseAzureOpenAIImages(
        this ITraconBuilder builder,
        Action<TraconImageOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.PostConfigure<TraconImageOptions>(static options => options.Provider ??= AzureOpenAIProviderNames.AzureOpenAI);
        services.TryAddKeyedSingleton<IImageGenerator>(AzureOpenAIProviderNames.AzureOpenAI, static (provider, _) =>
        {
            var options = provider.GetRequiredService<IOptions<TraconImageOptions>>().Value;
            var deploymentName = options.Model
                ?? throw new TraconException(
                    $"{nameof(TraconImageOptions)}.{nameof(TraconImageOptions.Model)} is required for Azure OpenAI image generation.");

            return provider.GetRequiredService<AzureOpenAIChatClientFactory>().CreateImageGenerator(deploymentName);
        });

        return builder;
    }
#pragma warning restore MEAI001
}
