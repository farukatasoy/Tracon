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
    /// <param name="configure">Optional settings of this provider: its model and its provider settings.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <remarks>
    /// <para>
    /// Call <see cref="OpenAIProviderExtensions.UseOpenAI(ITraconBuilder, string, Action{OpenAIProviderOptions}?)"/>
    /// first. This call shares the authenticated OpenAI client that <c>UseOpenAI</c>
    /// registers and does not add a package or a second connection pool.
    /// </para>
    /// <para>
    /// <paramref name="configure"/> changes the entry of <c>openai</c> in <see cref="TraconImageOptions.Providers"/> and no
    /// other: another image provider registered beside this one keeps its own model. <c>openai.quality</c> in
    /// <see cref="TraconImageProviderOptions.ProviderSettings"/> sets the image quality.
    /// </para>
    /// <para>
    /// An image call is billed and shares the client of <c>UseOpenAI</c>, so it is sent again by that client's retry count
    /// (<see cref="OpenAIProviderOptions.MaxRetries"/>, three by default). <see cref="TraconImageProviderOptions.MaxRetries"/>
    /// lowers it for image calls alone: <c>0</c> sends a call once, and chat calls are not affected.
    /// </para>
    /// <para>
    /// The <c>generate_image</c> tool stays absent until
    /// <see cref="TraconImageOptions.Enabled"/> is true. Registering this
    /// generator alone does not expose a paid capability to agents.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseOpenAI(builder.Configuration.GetSection(OpenAIProviderOptions.SectionName))
    ///        .UseOpenAIImages(provider =&gt; provider.Model = "gpt-image-1")
    ///        .ConfigureImages(images =&gt; images.Enabled = true);
    /// </code>
    /// </example>
    /// </remarks>
#pragma warning disable MEAI001
    public static ITraconBuilder UseOpenAIImages(
        this ITraconBuilder builder,
        Action<TraconImageProviderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        ImageProviderRegistrations.Register(services, OpenAIProviderNames.ChatCompletions);

        if (configure is not null)
        {
            services.Configure<TraconImageOptions>(
                options => configure(ImageProviderRegistrations.ProviderOptions(options, OpenAIProviderNames.ChatCompletions)));
        }

        services.TryAddKeyedSingleton<IImageGenerator>(OpenAIProviderNames.ChatCompletions, static (provider, _) =>
        {
            var model = ImageProviderRegistrations.RequireModel(provider, OpenAIProviderNames.ChatCompletions, "OpenAI image generation");

            return provider.GetRequiredService<OpenAINamedChatClientFactoryCache>().Setup(Options.DefaultName).CreateImageGenerator(model);
        });

        return builder;
    }
#pragma warning restore MEAI001
}
