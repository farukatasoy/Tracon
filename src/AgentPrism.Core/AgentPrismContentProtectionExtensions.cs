using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentPrism;

/// <summary>Chain extensions that turn on at-rest content protection.</summary>
/// <remarks>
/// This is an extension method, not a member of <see cref="IAgentPrismBuilder"/>:
/// adding a member to the interface is a breaking change after release; adding
/// an extension method is not.
/// </remarks>
public static class AgentPrismContentProtectionExtensions
{
    /// <summary>Registers AgentPrism's built-in AES-256-GCM content protector.</summary>
    /// <param name="builder">The configuration chain.</param>
    /// <param name="configure">Key and column settings.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <strong>This call is the no-surprises rule's gate.</strong> <c>AddAgentPrism()</c>
    /// alone registers a no-op protector that writes plaintext unchanged; without
    /// this call (or a matching <c>AgentPrism:ContentProtection:Enabled</c>
    /// configuration section), nothing is encrypted.
    /// </para>
    /// <para>
    /// Replaces the default registration rather than adding to it — a
    /// consumer's explicit call always wins over AgentPrism's own default
    /// (the same rule <c>UsePostgreSql</c> follows for its stores).
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
    ///        .AddContentProtection(options =>
    ///        {
    ///            options.ActiveKeyId = "2026-08";
    ///            options.Keys["2026-08"] = "ContentProtectionKeys:2026-08";
    ///        });
    /// </code>
    /// </example>
    /// </remarks>
    public static IAgentPrismBuilder AddContentProtection(
        this IAgentPrismBuilder builder,
        Action<AgentPrismContentProtectionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.Replace(ServiceDescriptor.Singleton<IContentProtector, AesGcmContentProtector>());

        if (configure is not null)
        {
            // Runs AFTER configuration binding: the value written in code
            // overrides the AgentPrism:ContentProtection section. Same order
            // as other settings (K4 — the caller's registration wins).
            builder.Services.Configure(configure);
        }

        return builder;
    }

    /// <summary>Registers your own <see cref="IContentProtector"/> implementation.</summary>
    /// <typeparam name="TProtector">The protector type.</typeparam>
    /// <param name="builder">The configuration chain.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <see cref="AgentPrismContentProtectionOptions.Columns"/> still governs
    /// which columns are protected, whichever implementation is registered;
    /// only the actual protect/unprotect logic is replaced.
    /// </remarks>
    public static IAgentPrismBuilder AddContentProtection<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProtector>(
        this IAgentPrismBuilder builder)
        where TProtector : class, IContentProtector
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.Replace(ServiceDescriptor.Singleton<IContentProtector, TProtector>());

        return builder;
    }
}
