using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentPrism;

/// <summary>Chain extensions that turn on content inspection — Phase 48.</summary>
/// <remarks>
/// This is an extension method, not a member of <see cref="IAgentPrismBuilder"/>:
/// adding a member to the interface is a breaking change after release; adding
/// an extension method is not.
/// </remarks>
public static class AgentPrismContentGuardBuilderExtensions
{
    /// <summary>
    /// Registers AgentPrism's built-in pattern-based content guard.
    /// </summary>
    /// <param name="builder">The configuration chain.</param>
    /// <param name="configure">Pattern and denied-term settings.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// 🚨 <strong>This call is K1's gate.</strong> <c>AddAgentPrism()</c> alone
    /// registers no guard, and the inspection wrapper is <em>never added</em> to
    /// the model pipeline. Without this call no prompt is inspected, no response
    /// is inspected, and no cost is paid.
    /// </para>
    /// <para>
    /// The built-in guard carries <strong>no rules by default</strong>:
    /// <see cref="PatternContentGuardOptions.MaskedPii"/> is
    /// <see cref="PiiPatterns.None"/> and
    /// <see cref="PatternContentGuardOptions.DeniedTerms"/> is empty. Which
    /// pattern family to turn on is an explicit choice; turning them all on at
    /// once compounds the false-positive risk.
    /// </para>
    /// <para>
    /// The same settings are also read from the <c>AgentPrism:ContentGuard:Pattern</c>
    /// configuration section; if that section is present the guard is already
    /// registered by <c>AddAgentPrism()</c> and this call is redundant (the
    /// registration uses <c>TryAddEnumerable</c>, so it is never added twice).
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
    ///        .AddPatternContentGuard(options =>
    ///        {
    ///            options.MaskedPii = PiiPatterns.CreditCard | PiiPatterns.Email;
    ///            options.DeniedTerms.Add("secret-project");
    ///        });
    /// </code>
    /// </example>
    /// </remarks>
    public static IAgentPrismBuilder AddPatternContentGuard(
        this IAgentPrismBuilder builder,
        Action<PatternContentGuardOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IContentGuard, PatternContentGuard>());

        if (configure is not null)
        {
            // Runs AFTER configuration binding: the value written in code
            // overrides the AgentPrism:ContentGuard:Pattern section. Same order
            // as other settings (K4 — the caller's registration wins).
            builder.Services.Configure(configure);
        }

        return builder;
    }

    /// <summary>
    /// Registers your own <see cref="IContentGuard"/> implementation.
    /// </summary>
    /// <typeparam name="TGuard">The guard type.</typeparam>
    /// <param name="builder">The configuration chain.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Multiple guards can be registered; all of them run in sequence and
    /// <strong>the strictest decision wins</strong>.
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
    ///        .AddPatternContentGuard()
    ///        .AddContentGuard&lt;CustomerNameGuard&gt;();
    /// </code>
    /// </example>
    /// </remarks>
    public static IAgentPrismBuilder AddContentGuard<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TGuard>(
        this IAgentPrismBuilder builder)
        where TGuard : class, IContentGuard
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IContentGuard, TGuard>());

        return builder;
    }
}
