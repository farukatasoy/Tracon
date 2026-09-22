using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Adds OpenAI image generation to the Tracon chain.</summary>
public static class OpenAIImageBuilderExtensions
{
    /// <summary>Registers the OpenAI image generator.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="configure">Optional image-tool settings.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <remarks>
    /// <para>
    /// Call <see cref="OpenAIProviderExtensions.UseOpenAI(ITraconBuilder, string, Action{OpenAIProviderOptions}?)"/>
    /// first. This call shares the authenticated OpenAI client that <c>UseOpenAI</c>
    /// registers and does not add a package or a second connection pool.
    /// </para>
    /// <para>
    /// The <c>generate_image</c> tool stays absent until
    /// <see cref="TraconImageOptions.Enabled"/> is true. Registering this
    /// generator alone does not expose a paid capability to agents.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseOpenAI(builder.Configuration["OpenAI:ApiKey"]!)
    ///        .UseOpenAIImages(options =&gt;
    ///        {
    ///            options.Enabled = true;
    ///            options.Model = "gpt-image-1";
    ///        });
    /// </code>
    /// </example>
    /// </remarks>
#pragma warning disable MEAI001
    public static ITraconBuilder UseOpenAIImages(
        this ITraconBuilder builder,
        Action<TraconImageOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.PostConfigure<TraconImageOptions>(static options => options.Provider ??= OpenAIProviderNames.ChatCompletions);
        services.TryAddKeyedSingleton<IImageGenerator>(OpenAIProviderNames.ChatCompletions, static (provider, _) =>
        {
            var options = provider.GetRequiredService<IOptions<TraconImageOptions>>().Value;
            var model = options.Model
                ?? throw new TraconException(
                    $"{nameof(TraconImageOptions)}.{nameof(TraconImageOptions.Model)} is required for OpenAI image generation.");

            return provider.GetRequiredService<OpenAIChatClientFactory>().CreateImageGenerator(model);
        });

        return builder;
    }
#pragma warning restore MEAI001
}
