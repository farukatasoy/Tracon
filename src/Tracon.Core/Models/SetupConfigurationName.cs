using Microsoft.Extensions.Configuration;

namespace Tracon;

/// <summary>
/// The start-up checks the four providers share for the configuration keys a host names for its key and its address.
/// </summary>
/// <remarks>
/// The host still starts with a key and an address, so a name that has no usable value yet fails the start the way a
/// missing literal does. The failure sentence names the configuration key and never a value.
/// </remarks>
internal static class SetupConfigurationName
{
    /// <summary>Tells whether the configuration key holds a key a header can carry.</summary>
    /// <param name="configuration">The configuration; <see langword="null"/> when none is registered.</param>
    /// <param name="name">The configuration key name; may be <see langword="null"/>.</param>
    /// <param name="optionName">The option the key is written under, as a refusal names it.</param>
    /// <returns><see langword="true"/> when the name holds a usable value.</returns>
    internal static bool GivesUsableKey(IConfiguration? configuration, string? name, string optionName)
    {
        if (string.IsNullOrWhiteSpace(name) || configuration?[name] is not { } value)
        {
            return false;
        }

        var trimmed = value.Trim();

        return trimmed.Length > 0 && OutboundApiKey.Refusal(optionName, trimmed) is null;
    }

    /// <summary>Tells whether the configuration key holds an absolute address.</summary>
    /// <param name="configuration">The configuration; <see langword="null"/> when none is registered.</param>
    /// <param name="name">The configuration key name; may be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the name holds an absolute address.</returns>
    internal static bool GivesEndpoint(IConfiguration? configuration, string? name)
        => !string.IsNullOrWhiteSpace(name)
            && configuration?[name] is { } value
            && Uri.TryCreate(value.Trim(), UriKind.Absolute, out _);

    /// <summary>The sentence that tells which configuration key the key is read from, for a failure about a missing key.</summary>
    /// <param name="name">The configuration key name; may be <see langword="null"/>.</param>
    /// <returns>An empty text when no name is given.</returns>
    internal static string Hint(string? name)
        => string.IsNullOrWhiteSpace(name)
            ? string.Empty
            : $" The configuration key '{name}' that the key is read from has no usable value either.";
}
