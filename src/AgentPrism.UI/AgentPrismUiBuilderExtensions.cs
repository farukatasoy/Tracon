using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentPrism;

/// <summary>
/// Provides extensions that register the embedded management UI.
/// </summary>
public static class AgentPrismUiBuilderExtensions
{
    /// <summary>
    /// Registers the embedded management UI. <c>MapAgentPrism()</c> finds the
    /// registration and binds the UI routes under the same prefix.
    /// </summary>
    /// <param name="builder">The AgentPrism configuration chain.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// No separate mapping call is required. The prefix is written in one place -
    /// inside <c>MapAgentPrism()</c> - because a prefix written in two places,
    /// once out of sync, would make the UI show a blank page without any error.
    /// </para>
    /// <para>
    /// Registration uses <c>TryAdd</c>. A consumer that registers its own
    /// <see cref="IAgentPrismUiProvider"/> implementation earlier wins (the replaceable-extension rule).
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
    ///        .UseOpenAI(apiKey)
    ///        .UseUI();
    ///
    /// app.MapAgentPrism("/agentprism");
    /// </code>
    /// </example>
    /// </remarks>
    public static IAgentPrismBuilder UseUI(this IAgentPrismBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton<IAgentPrismUiProvider, EmbeddedUiProvider>();

        return builder;
    }
}
