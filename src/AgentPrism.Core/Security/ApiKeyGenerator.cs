using System.Security.Cryptography;
using System.Text;

namespace AgentPrism;

/// <summary>A helper that produces a raw API key value and its hash.</summary>
/// <remarks>
/// <para>
/// Format: <c>ap_{tenant-prefix}_{32-byte-base64url}</c>
/// The prefix exists only
/// for READABILITY; it is not used for authentication — lookup always goes
/// through the hash produced by <see cref="ComputeHash"/>.
/// </para>
/// <para>
/// The hash algorithm is SHA-256, not Argon2. The key is
/// a RANDOM 32-byte value, not a user password; a dictionary attack does not
/// apply, and a slow hash would only add latency to every request.
/// </para>
/// </remarks>
public static class ApiKeyGenerator
{
    private const string KeyPrefixMarker = "ap_";
    private const int SecretByteLength = 32;
    private const int DisplayPrefixLength = 12;
    private const int MaxTenantSegmentLength = 12;

    /// <summary>Generates a new raw key.</summary>
    /// <param name="tenantId">The tenant the key attaches to (read only for the prefix).</param>
    /// <returns>The raw value, its hash, and a display prefix.</returns>
    /// <exception cref="ArgumentException"><paramref name="tenantId"/> is empty.</exception>
    public static GeneratedApiKey Generate(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        Span<byte> secretBytes = stackalloc byte[SecretByteLength];
        RandomNumberGenerator.Fill(secretBytes);

        var plaintext = $"{KeyPrefixMarker}{SanitizeTenantSegment(tenantId)}_{ToBase64Url(secretBytes)}";

        return new GeneratedApiKey
        {
            PlaintextKey = plaintext,
            KeyHash = ComputeHash(plaintext),
            KeyPrefix = plaintext[..Math.Min(DisplayPrefixLength, plaintext.Length)],
        };
    }

    /// <summary>Computes the SHA-256 hash of a raw key value.</summary>
    /// <param name="plaintextKey">The raw value.</param>
    /// <returns>The 32-byte hash.</returns>
    /// <exception cref="ArgumentException"><paramref name="plaintextKey"/> is empty.</exception>
    public static byte[] ComputeHash(string plaintextKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintextKey);

        return SHA256.HashData(Encoding.UTF8.GetBytes(plaintextKey));
    }

    private static string SanitizeTenantSegment(string tenantId)
    {
        Span<char> buffer = stackalloc char[Math.Min(tenantId.Length, MaxTenantSegmentLength)];
        var written = 0;

        foreach (var ch in tenantId)
        {
            if (written == buffer.Length)
            {
                break;
            }

            if (char.IsAsciiLetterOrDigit(ch))
            {
                buffer[written++] = char.ToLowerInvariant(ch);
            }
        }

        return written == 0 ? "default" : new string(buffer[..written]);
    }

    private static string ToBase64Url(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>The generated raw API key value and its derived forms.</summary>
public sealed record GeneratedApiKey
{
    /// <summary>The raw value. Visible only at creation time.</summary>
    public required string PlaintextKey { get; init; }

    /// <summary>The SHA-256 hash of the raw value. The only form written to the store.</summary>
    public required byte[] KeyHash { get; init; }

    /// <summary>The first characters of the raw value; used to distinguish it in a list.</summary>
    public required string KeyPrefix { get; init; }

    /// <summary>Returns a description that carries no raw key.</summary>
    /// <returns>The type name and the prefix; never the raw value.</returns>
    /// <remarks>
    /// A record's compiler-generated <c>ToString</c> prints every property. The
    /// prefix is safe to show - it is what the list view displays - but the raw
    /// value is not.
    /// </remarks>
    public override string ToString()
        => $"{nameof(GeneratedApiKey)} {{ KeyPrefix = {KeyPrefix}, PlaintextKey = [redacted] }}";
}
