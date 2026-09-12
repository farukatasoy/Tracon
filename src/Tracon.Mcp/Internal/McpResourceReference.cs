using System.Diagnostics.CodeAnalysis;

namespace Tracon;

/// <summary>
/// Parsing of <see cref="AgentDefinition.McpResourceUris"/> entries in the form
/// <c>"{server}:{uri}"</c>.
/// </summary>
/// <remarks>
/// A server name only contains <c>[a-zA-Z0-9_-]</c>. See <see cref="McpToolNaming"/>.
/// The first <c>:</c> is therefore a safe separator, while the resource URI itself,
/// such as <c>https://...</c> or <c>file:///...</c>, can freely contain colons.
/// </remarks>
internal static class McpResourceReference
{
    /// <summary>Splits a reference into a server name and resource URI.</summary>
    /// <returns><see langword="false"/> when separator <c>:</c> is missing or either part is empty.</returns>
    public static bool TryParse(string reference, [NotNullWhen(true)] out string? serverName, [NotNullWhen(true)] out string? uri)
    {
        var separator = reference.IndexOf(':', StringComparison.Ordinal);

        if (separator <= 0 || separator == reference.Length - 1)
        {
            serverName = null;
            uri = null;

            return false;
        }

        serverName = reference[..separator];
        uri = reference[(separator + 1)..];

        return true;
    }
}
