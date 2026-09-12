using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tracon;

/// <summary>
/// Provides extensions that register the embedded management UI.
/// </summary>
public static class TraconUiBuilderExtensions
{
    /// <summary>
    /// Registers the embedded management UI. <c>MapTracon()</c> finds the
    /// registration and binds the UI routes under the same prefix.
    /// </summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// No separate mapping call is required. The prefix is written in one place -
    /// inside <c>MapTracon()</c> - because a prefix written in two places,
    /// once out of sync, would make the UI show a blank page without any error.
    /// </para>
    /// <para>
    /// Registration uses <c>TryAdd</c>. A consumer that registers its own
    /// <see cref="ITraconUiProvider"/> implementation earlier wins (the replaceable-extension rule).
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseOpenAI(apiKey)
    ///        .UseUI();
    ///
    /// app.MapTracon("/tracon");
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder UseUI(this ITraconBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton<ITraconUiProvider, EmbeddedUiProvider>();

        return builder;
    }
}
