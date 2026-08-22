using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Adds Azure OpenAI image generation to the AgentPrism chain.</summary>
public static class AzureOpenAIImageBuilderExtensions
{
    /// <summary>Registers the Azure OpenAI image generator.</summary>
    /// <param name="builder">The AgentPrism chain.</param>
    /// <param name="configure">Optional image-tool settings.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <remarks>
    /// Call <see cref="AzureOpenAIProviderExtensions.UseAzureOpenAI(IAgentPrismBuilder, Uri, string, Action{AzureOpenAIProviderOptions}?)"/>
    /// first. The image model is an Azure deployment name, not a public model id.
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
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
    public static IAgentPrismBuilder UseAzureOpenAIImages(
        this IAgentPrismBuilder builder,
        Action<AgentPrismImageOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.PostConfigure<AgentPrismImageOptions>(static options => options.Provider ??= AzureOpenAIProviderNames.AzureOpenAI);
        services.TryAddKeyedSingleton<IImageGenerator>(AzureOpenAIProviderNames.AzureOpenAI, static (provider, _) =>
        {
            var options = provider.GetRequiredService<IOptions<AgentPrismImageOptions>>().Value;
            var deploymentName = options.Model
                ?? throw new AgentPrismException(
                    $"{nameof(AgentPrismImageOptions)}.{nameof(AgentPrismImageOptions.Model)} is required for Azure OpenAI image generation.");

            return provider.GetRequiredService<AzureOpenAIChatClientFactory>().CreateImageGenerator(deploymentName);
        });

        return builder;
    }
#pragma warning restore MEAI001
}
