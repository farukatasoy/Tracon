using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Adds Google image generation to the Tracon chain.</summary>
public static class GoogleImageBuilderExtensions
{
    /// <summary>Registers the Google image generator.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="configure">Optional image-tool settings.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <remarks>
    /// <para>
    /// Call <see cref="GoogleProviderExtensions.UseGoogle(ITraconBuilder, string, Action{GoogleProviderOptions}?)"/>
    /// first. Google.GenAI has no Microsoft.Extensions.AI image adapter, so this
    /// call registers Tracon's narrow GenerateAsync-only adapter.
    /// </para>
    /// <para>
    /// <strong>The model must be one that generates images through
    /// <c>generateContent</c></strong>, such as a Gemini image model. The older
    /// Imagen <c>predict</c> surface is not served by the Gemini API and a
    /// model that only supports it fails every request.
    /// </para>
    /// <para>
    /// The adapter generates from a prompt only. <c>Count</c> is passed as the
    /// candidate count and an image model commonly returns one image whatever
    /// you ask for. A requested media type is passed to the model and the
    /// answer is <em>checked</em>: if the model returns another format the call
    /// fails rather than handing back bytes that do not match what was asked
    /// for. Omit it to accept the model's own choice.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseGoogle(builder.Configuration["Google:ApiKey"]!)
    ///        .UseGoogleImages(options =&gt;
    ///        {
    ///            options.Enabled = true;
    ///            options.Model = "gemini-2.5-flash-image";
    ///        });
    /// </code>
    /// </example>
    /// </remarks>
#pragma warning disable MEAI001
    public static ITraconBuilder UseGoogleImages(
        this ITraconBuilder builder,
        Action<TraconImageOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.PostConfigure<TraconImageOptions>(static options => options.Provider ??= GoogleProviderNames.Google);
        services.TryAddKeyedSingleton<IImageGenerator>(GoogleProviderNames.Google, static (provider, _) =>
        {
            _ = provider.GetRequiredService<IOptions<TraconImageOptions>>().Value;
            return provider.GetRequiredService<GoogleChatClientFactory>().CreateImageGenerator();
        });

        return builder;
    }
#pragma warning restore MEAI001
}
