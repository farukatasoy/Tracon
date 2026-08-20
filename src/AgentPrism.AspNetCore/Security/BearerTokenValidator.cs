using System.Security.Cryptography;
using System.Text;

namespace AgentPrism;

/// <summary>
/// Compares the <c>Authorization: Bearer</c> header with the expected token in constant
/// time.
/// </summary>
/// <remarks>
/// <para>
/// The comparison first takes the SHA-256 digest of both values, then matches the digests
/// with
/// <see cref="CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>.
/// Comparing the raw bytes directly would <strong>leak the length</strong>: with inputs of
/// different length <c>FixedTimeEquals</c> returns immediately and an attacker can measure
/// the token length. A digest is always 32 bytes, so the duration is independent of the
/// input.
/// </para>
/// </remarks>
internal static class BearerTokenValidator
{
    private const string BearerPrefix = "Bearer ";

    /// <summary>Determines whether the header carries the expected token.</summary>
    /// <param name="authorizationHeader">The raw <c>Authorization</c> header.</param>
    /// <param name="expectedToken">The expected token.</param>
    /// <returns><see langword="true"/> when the header is valid.</returns>
    public static bool IsValid(string? authorizationHeader, string expectedToken)
    {
        if (string.IsNullOrEmpty(authorizationHeader) ||
            !authorizationHeader.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        return IsValidToken(authorizationHeader.AsSpan(BearerPrefix.Length).Trim(), expectedToken);
    }

    /// <summary>Compares a raw token value with the expected token.</summary>
    /// <param name="presented">The presented token.</param>
    /// <param name="expectedToken">The expected token.</param>
    /// <returns><see langword="true"/> when the values are equal.</returns>
    /// <remarks>
    /// This exists for the WebSocket handshake: a browser <strong>cannot</strong> add an
    /// <c>Authorization</c> header to a <c>&lt;script&gt;</c> request or to a socket upgrade,
    /// so the token travels in the <c>Sec-WebSocket-Protocol</c> sub-protocol and arrives
    /// here bare. The comparison is still constant time.
    /// </remarks>
    public static bool IsValidToken(ReadOnlySpan<char> presented, string expectedToken)
    {
        if (presented.IsEmpty)
        {
            return false;
        }

        Span<byte> presentedHash = stackalloc byte[SHA256.HashSizeInBytes];
        Span<byte> expectedHash = stackalloc byte[SHA256.HashSizeInBytes];

        HashUtf8(presented, presentedHash);
        HashUtf8(expectedToken.AsSpan(), expectedHash);

        return CryptographicOperations.FixedTimeEquals(presentedHash, expectedHash);
    }

    /// <summary>Extracts the raw token value from an <c>Authorization</c> header.</summary>
    /// <param name="authorizationHeader">The raw header.</param>
    /// <returns>The token; <see langword="null"/> when the header carries no <c>Bearer</c> scheme or is empty.</returns>
    /// <remarks>
    /// This does NOT validate the value — it only extracts it. The caller uses it to try the
    /// API key path after the static token comparison.
    /// </remarks>
    public static string? TryExtractToken(string? authorizationHeader)
    {
        if (string.IsNullOrEmpty(authorizationHeader) ||
            !authorizationHeader.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var candidate = authorizationHeader[BearerPrefix.Length..].Trim();

        return candidate.Length == 0 ? null : candidate;
    }

    private static void HashUtf8(ReadOnlySpan<char> value, Span<byte> destination)
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        var buffer = byteCount <= 256 ? stackalloc byte[256] : new byte[byteCount];
        var written = Encoding.UTF8.GetBytes(value, buffer);

        SHA256.HashData(buffer[..written], destination);

        // Do not leave the token bytes on the stack.
        CryptographicOperations.ZeroMemory(buffer[..written]);
    }
}
