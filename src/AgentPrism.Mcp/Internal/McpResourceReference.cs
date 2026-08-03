using System.Diagnostics.CodeAnalysis;

namespace AgentPrism;

/// <summary>
/// <see cref="AgentDefinition.McpResourceUris"/> ogelerinin (<c>"{sunucu}:{uri}"</c>)
/// ayristirilmasi.
/// </summary>
/// <remarks>
/// Sunucu adi yalniz <c>[a-zA-Z0-9_-]</c> icerebilir (bkz. <see cref="McpToolNaming"/>),
/// bu yuzden ayirici olarak ilk <c>:</c> guvenlidir — kaynak URI'sinin kendisi
/// (<c>https://...</c>, <c>file:///...</c>) serbestce kolon tasiyabilir.
/// </remarks>
internal static class McpResourceReference
{
    /// <summary>Bir referansi sunucu adi ve kaynak URI'sine ayirir.</summary>
    /// <returns>Ayirici <c>:</c> bulunamazsa veya taraflardan biri bossa <see langword="false"/>.</returns>
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
