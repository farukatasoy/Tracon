using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Adds Google image generation to the AgentPrism chain.</summary>
public static class GoogleImageBuilderExtensions
{
    /// <summary>Registers the Google image generator.</summary>
    /// <param name="builder">The AgentPrism chain.</param>
    /// <param name="configure">Optional image-tool settings.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <remarks>
    /// Call <see cref="GoogleProviderExtensions.UseGoogle(IAgentPrismBuilder, string, Action{GoogleProviderOptions}?)"/>
    /// first. Google.GenAI has no Microsoft.Extensions.AI image adapter, so this
    /// call registers AgentPrism's narrow GenerateAsync-only adapter.
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
    ///        .UseGoogle(builder.Configuration["Google:ApiKey"]!)
    ///        .UseGoogleImages(options =&gt;
    ///        {
    ///            options.Enabled = true;
    ///            options.Model = "your-imagen-model";
    ///        });
    /// </code>
    /// </example>
    /// </remarks>
#pragma warning disable MEAI001
    public static IAgentPrismBuilder UseGoogleImages(
        this IAgentPrismBuilder builder,
        Action<AgentPrismImageOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.PostConfigure<AgentPrismImageOptions>(static options => options.Provider ??= GoogleProviderNames.Google);
        services.TryAddKeyedSingleton<IImageGenerator>(GoogleProviderNames.Google, static (provider, _) =>
        {
            _ = provider.GetRequiredService<IOptions<AgentPrismImageOptions>>().Value;
            return provider.GetRequiredService<GoogleChatClientFactory>().CreateImageGenerator();
        });

        return builder;
    }
#pragma warning restore MEAI001
}
