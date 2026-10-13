using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

/// <summary>
/// One named speech provider: the record the resolver lists, and the key its
/// instance is registered under.
/// </summary>
/// <remarks>
/// <para>
/// The key is this record, not the bare name. A consumer that registers its own
/// keyed <see cref="ISpeechSynthesizer"/> under the same string does not collide with
/// it, and a named provider cannot be resolved past the tenant checks with
/// <c>GetKeyedService</c>.
/// </para>
/// <para>
/// The registration is kept because the container cannot list the keys a service
/// type was registered under.
/// </para>
/// </remarks>
/// <param name="Kind">Whether the provider synthesizes or transcribes.</param>
/// <param name="Name">The normalized (lower-case) registration name.</param>
internal sealed record SpeechProviderRegistration(SpeechProviderKind Kind, string Name)
{
    /// <summary>Checks a name and returns the registration it would make.</summary>
    /// <param name="services">The service collection the registration goes into.</param>
    /// <param name="kind">The provider kind.</param>
    /// <param name="name">The name as the caller wrote it.</param>
    /// <returns>The registration.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not a valid speech provider name.</exception>
    /// <exception cref="InvalidOperationException">The name is already registered for <paramref name="kind"/>.</exception>
    public static SpeechProviderRegistration Create(IServiceCollection services, SpeechProviderKind kind, string name)
    {
        if (!SpeechProviderNames.TryNormalize(name, out var normalized))
        {
            throw new ArgumentException(SpeechProviderNames.InvalidNameMessage, nameof(name));
        }

        var registration = new SpeechProviderRegistration(kind, normalized);

        // Rejected here, at setup: a second keyed registration under the same key
        // would silently win resolution by registration order.
        foreach (var descriptor in services)
        {
            if (!descriptor.IsKeyedService
                && descriptor.ServiceType == typeof(SpeechProviderRegistration)
                && descriptor.ImplementationInstance is SpeechProviderRegistration existing
                && existing == registration)
            {
                throw new InvalidOperationException(
                    $"A speech {Noun(kind)} named '{normalized}' is already registered. Each name selects " +
                    "one provider; register the second one under another name.");
            }
        }

        return registration;
    }

    /// <summary>Gets the noun for a kind, as messages write it.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns><c>synthesizer</c> or <c>transcriber</c>.</returns>
    public static string Noun(SpeechProviderKind kind)
        => kind == SpeechProviderKind.Transcriber ? "transcriber" : "synthesizer";
}

/// <summary>The rule for a speech provider's registration name.</summary>
internal static class SpeechProviderNames
{
    /// <summary>The longest accepted name.</summary>
    public const int MaxLength = 64;

    /// <summary>The message for a name the rule rejects. It does not repeat the input.</summary>
    public const string InvalidNameMessage =
        "A speech provider name is 1 to 64 characters of a-z, 0-9, '.', '_' or '-' (compared in lower case).";

    /// <summary>Normalizes a name and checks it.</summary>
    /// <param name="name">The name as written.</param>
    /// <param name="normalized">The lower-case name when it is valid.</param>
    /// <returns><see langword="true"/> when the name is valid.</returns>
    public static bool TryNormalize(string? name, [NotNullWhen(true)] out string? normalized)
    {
        normalized = null;

        if (name is not { Length: > 0 and <= MaxLength })
        {
            return false;
        }

        var lower = name.ToLowerInvariant();

        foreach (var character in lower)
        {
            if (!(char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '.' or '_' or '-'))
            {
                return false;
            }
        }

        normalized = lower;

        return true;
    }
}
