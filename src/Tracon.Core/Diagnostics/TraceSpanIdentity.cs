using System.Security.Cryptography;
using System.Text;

namespace Tracon;

/// <summary>
/// Derives deterministic database identifiers from W3C trace and span identifiers.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why derive identifiers?</strong> A parent can still be incomplete when a
/// span completes. Matching unordered spans with a map would need state and parent
/// waiting logic. With a derived identifier, the parent identifier is <em>computable</em>,
/// so no map is needed.
/// </para>
/// <para>
/// An additional benefit is that writing the same span twice produces the same identifier,
/// making the write operation conceptually idempotent.
/// </para>
/// <para>
/// SHA-256 is used for <em>collision-free distribution</em>, not cryptography. The
/// input is already 24 random bytes, so truncation does not collide in practice.
/// </para>
/// </remarks>
internal static class TraceSpanIdentity
{
    /// <summary>
    /// Produces the database identifier for a span.
    /// </summary>
    /// <param name="traceId">The W3C trace identifier, with 32 hexadecimal characters.</param>
    /// <param name="spanId">The W3C span identifier, with 16 hexadecimal characters.</param>
    /// <returns>The deterministic identifier.</returns>
    public static Guid ForSpan(string traceId, string spanId)
    {
        var length = Encoding.UTF8.GetByteCount(traceId) + 1 + Encoding.UTF8.GetByteCount(spanId);
        var buffer = length <= 128 ? stackalloc byte[length] : new byte[length];

        var written = Encoding.UTF8.GetBytes(traceId, buffer);
        buffer[written++] = (byte)':';
        Encoding.UTF8.GetBytes(spanId, buffer[written..]);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(buffer, hash);

        return new Guid(hash[..16]);
    }
}
