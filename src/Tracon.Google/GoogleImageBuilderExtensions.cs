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
    /// <param name="configure">Optional settings of this provider: its model.</param>
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
    /// <paramref name="configure"/> changes the entry of <c>google</c> in <see cref="TraconImageOptions.Providers"/> and no
    /// other. The provider has no setting of its own: <see cref="TraconImageProviderOptions.ProviderSettings"/> must stay empty,
    /// and so must <see cref="TraconImageProviderOptions.MaxRetries"/>. The Google image client has no retry setting to lower,
    /// so a value there fails at startup instead of being ignored.
    /// </para>
    /// <para>
    /// <strong>A preview image model needs <c>GoogleProviderOptions.ApiVersion = "v1beta"</c>.</strong> The default <c>v1</c>
    /// does not list the preview image models, and a call to one fails with a "not found for API version v1" answer. The
    /// version is the provider's, shared by chat and images: raising it moves the chat models to <c>v1beta</c> too.
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
    ///        .UseGoogle(builder.Configuration.GetSection(GoogleProviderOptions.SectionName))
    ///        .UseGoogleImages(provider =&gt; provider.Model = "gemini-2.5-flash-image")
    ///        .ConfigureImages(images =&gt; images.Enabled = true);
    /// </code>
    /// </example>
    /// </remarks>
#pragma warning disable MEAI001
    public static ITraconBuilder UseGoogleImages(
        this ITraconBuilder builder,
        Action<TraconImageProviderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        ImageProviderRegistrations.Register(services, GoogleProviderNames.Google);

        if (configure is not null)
        {
            services.Configure<TraconImageOptions>(
                options => configure(ImageProviderRegistrations.ProviderOptions(options, GoogleProviderNames.Google)));
        }

        GoogleProviderRequirement.Require(services, nameof(UseGoogleImages));

        services.TryAddKeyedSingleton<IImageGenerator>(GoogleProviderNames.Google, static (provider, _) =>
        {
            _ = provider.GetRequiredService<IOptions<TraconImageOptions>>().Value;

            // One instance for the life of the host: the image resolver keeps one telemetry ring for each registered
            // instance. A host that names the configuration keys of the key and the address gets the generator that
            // picks the client of the current ones when a call starts.
            var setup = provider.GetRequiredService<GoogleSetup>();

            return setup.Live is null
                ? provider.GetRequiredService<GoogleChatClientFactory>().CreateImageGenerator()
                : new GoogleImageGenerator(() => setup.Current().Client);
        });

        return builder;
    }
#pragma warning restore MEAI001
}
