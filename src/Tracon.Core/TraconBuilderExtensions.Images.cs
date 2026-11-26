using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

public static partial class TraconBuilderExtensions
{
    /// <summary>Changes the image-generation settings.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="configure">The change.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// The same as <c>services.Configure&lt;TraconImageOptions&gt;(configure)</c>, in the chain. The calls that register an image
    /// provider (<c>UseOpenAIImages</c>, <c>UseAzureOpenAIImages</c>, <c>UseGoogleImages</c>) change the settings of their own
    /// provider and nothing else; what is shared between providers (whether the tool is enabled, which provider it draws with,
    /// the count and the timeout) is set here or in configuration.
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
    public static ITraconBuilder ConfigureImages(this ITraconBuilder builder, Action<TraconImageOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.Configure(configure);

        return builder;
    }
}
