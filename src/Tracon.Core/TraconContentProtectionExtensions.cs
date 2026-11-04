using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tracon;

/// <summary>Chain extensions that turn on at-rest content protection.</summary>
/// <remarks>
/// This is an extension method, not a member of <see cref="ITraconBuilder"/>:
/// adding a member to the interface is a breaking change after release; adding
/// an extension method is not.
/// </remarks>
public static class TraconContentProtectionExtensions
{
    /// <summary>Registers Tracon's built-in AES-256-GCM content protector.</summary>
    /// <param name="builder">The configuration chain.</param>
    /// <param name="configure">
    /// Key and column settings. <see cref="TraconContentProtectionOptions.Enabled"/> is <see langword="true"/> when
    /// this delegate runs, so a delegate that only names the keys turns protection on; it can write
    /// <see langword="false"/> to turn it off again.
    /// </param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <strong>This call is the no-surprises rule's gate.</strong> <c>AddTracon()</c>
    /// alone registers a no-op protector that writes plaintext unchanged; without
    /// this call (or a matching <c>Tracon:ContentProtection:Enabled</c>
    /// configuration section), nothing is encrypted.
    /// </para>
    /// <para>
    /// With a <paramref name="configure"/> delegate the call also turns protection on:
    /// <see cref="TraconContentProtectionOptions.Enabled"/> is set to <see langword="true"/>
    /// before the delegate runs, after configuration binding, so the example below encrypts
    /// and a delegate that writes <c>options.Enabled = false</c> keeps the keys and switches
    /// protection off. Without a delegate the call only registers the protector and
    /// <c>Tracon:ContentProtection:Enabled</c> decides.
    /// </para>
    /// <para>
    /// Replaces the default registration rather than adding to it — a
    /// consumer's explicit call always wins over Tracon's own default
    /// (the same rule <c>UsePostgreSql</c> follows for its stores).
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddContentProtection(options =>
    ///        {
    ///            options.ActiveKeyId = "2026-08";
    ///            options.Keys["2026-08"] = "ContentProtectionKeys:2026-08";
    ///        });
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddContentProtection(
        this ITraconBuilder builder,
        Action<TraconContentProtectionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.Replace(ServiceDescriptor.Singleton<IContentProtector, AesGcmContentProtector>());

        if (configure is not null)
        {
            // Runs AFTER configuration binding: the value written in code
            // overrides the Tracon:ContentProtection section. Same order
            // as other settings (the caller's registration wins).
            // A delegate that hands over the keys is a request for protection:
            // the documented example names a key id and a key name and nothing
            // else, and it used to register the protector and encrypt nothing.
            builder.Services.Configure<TraconContentProtectionOptions>(options =>
            {
                options.Enabled = true;
                configure(options);
            });
        }

        return builder;
    }

    /// <summary>Registers your own <see cref="IContentProtector"/> implementation.</summary>
    /// <typeparam name="TProtector">The protector type.</typeparam>
    /// <param name="builder">The configuration chain.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <see cref="TraconContentProtectionOptions.Columns"/> still governs
    /// which columns are protected, whichever implementation is registered;
    /// only the actual protect/unprotect logic is replaced.
    /// </remarks>
    public static ITraconBuilder AddContentProtection<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProtector>(
        this ITraconBuilder builder)
        where TProtector : class, IContentProtector
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.Replace(ServiceDescriptor.Singleton<IContentProtector, TProtector>());

        return builder;
    }
}
