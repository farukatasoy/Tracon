using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AgentPrism;

/// <summary>Produces and verifies the HMAC-SHA256 signature of webhook requests.</summary>
/// <remarks>
/// <para>
/// The signature is computed from the string: <c>{timestamp}.{body}</c>. The
/// timestamp is <strong>included</strong> in the signature; otherwise a
/// captured request could be replayed forever (K-163).
/// </para>
/// <para>
/// When verifying the signature, the recipient must also check that the
/// timestamp is within its own tolerance. AgentPrism cannot enforce this; it
/// is documented in the README.
/// </para>
/// </remarks>
public static class WebhookSigner
{
    /// <summary>The header that carries the event name.</summary>
    public const string EventHeader = "X-AgentPrism-Event";

    /// <summary>The header that carries the delivery identity. Used to distinguish duplicate deliveries.</summary>
    public const string DeliveryHeader = "X-AgentPrism-Delivery";

    /// <summary>The header that carries the timestamp, in Unix seconds.</summary>
    public const string TimestampHeader = "X-AgentPrism-Timestamp";

    /// <summary>The header that carries the signature.</summary>
    public const string SignatureHeader = "X-AgentPrism-Signature";

    /// <summary>Produces a signature for a body and timestamp.</summary>
    /// <param name="body">The JSON body to send.</param>
    /// <param name="timestamp">The request's timestamp.</param>
    /// <param name="secret">The signing secret.</param>
    /// <returns>The signature, in the form <c>sha256=&lt;hex&gt;</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="secret"/> is empty.</exception>
    public static string Sign(string body, DateTimeOffset timestamp, string secret)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        var unixSeconds = timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signedPayload = $"{unixSeconds}.{body}";

        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(signedPayload));

        // Convert.ToHexStringLower arrived in .NET 9; this package also targets net8.0.
        return $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    /// <summary>Verifies a signature.</summary>
    /// <param name="body">The received body.</param>
    /// <param name="timestamp">The timestamp in the request.</param>
    /// <param name="secret">The signing secret.</param>
    /// <param name="signature">The signature in the request.</param>
    /// <returns><see langword="true"/> if the signature is valid.</returns>
    /// <remarks>
    /// The comparison is constant-time, using
    /// <see cref="CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>:
    /// an ordinary string comparison would let the signature be guessed byte
    /// by byte from the time taken until the first differing byte.
    /// </remarks>
    public static bool Verify(string body, DateTimeOffset timestamp, string secret, string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var expected = Sign(body, timestamp, secret);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature));
    }
}
