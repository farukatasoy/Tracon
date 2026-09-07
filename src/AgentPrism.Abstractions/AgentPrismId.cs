using System.Security.Cryptography;

namespace AgentPrism;

/// <summary>
/// UUID version 7 generator for AgentPrism identifiers (RFC 9562).
/// </summary>
/// <remarks>
/// <para>
/// A UUIDv7 is time ordered: the first 48 bits are a Unix millisecond stamp.
/// This keeps B-tree indexes from fragmenting and makes <c>ORDER BY id</c>
/// return time order. It also avoids the central sequence bottleneck of
/// <c>bigserial</c>.
/// </para>
/// <para>
/// We use our own implementation instead of <c>Guid.CreateVersion7()</c> from
/// .NET 9. The reason: the package also targets <c>net8.0</c>, and storage keys
/// must be produced the same way on every target.
/// </para>
/// </remarks>
public static class AgentPrismId
{
    /// <summary>Creates a new UUIDv7 stamped with the current time.</summary>
    /// <returns>A time-ordered identifier.</returns>
    public static Guid NewId() => NewId(DateTimeOffset.UtcNow);

    /// <summary>Creates a UUIDv7 stamped with the given time.</summary>
    /// <param name="timestamp">The time stamp to embed in the identifier.</param>
    /// <returns>A time-ordered identifier.</returns>
    public static Guid NewId(DateTimeOffset timestamp)
    {
        // 6-15: random.
        Span<byte> random = stackalloc byte[10];
        RandomNumberGenerator.Fill(random);

        return Compose(timestamp, random);
    }

    /// <summary>
    /// Creates a UUIDv7 that depends only on its inputs: the same time stamp
    /// and seed always produce the same identifier.
    /// </summary>
    /// <param name="timestamp">
    /// The time stamp to embed. Must itself be stable across calls — a stored
    /// creation time, never <see cref="DateTimeOffset.UtcNow"/>.
    /// </param>
    /// <param name="seed">The value the identifier is derived from.</param>
    /// <returns>A time-ordered identifier that can be recomputed.</returns>
    /// <remarks>
    /// <para>
    /// For a write that must be re-drivable after a crash. Where
    /// <see cref="NewId()"/> would mint a second identifier on a retry — and
    /// so a second row — a derived one lets the retry land on exactly the row
    /// the interrupted attempt was creating, without a table to remember what
    /// that row was going to be.
    /// </para>
    /// <para>
    /// Still a real UUIDv7, so it keeps the index locality
    /// <see cref="NewId()"/> is chosen for: only the random bytes are replaced,
    /// by a SHA-256 digest of <paramref name="seed"/>. It is a derivation, not
    /// a signature — the seed is not recoverable, but neither is it a secret.
    /// </para>
    /// </remarks>
    public static Guid DeriveId(DateTimeOffset timestamp, string seed)
    {
        ArgumentNullException.ThrowIfNull(seed);

        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed), digest);

        return Compose(timestamp, digest[..10]);
    }

    /// <summary>Builds a UUIDv7 from a time stamp and ten bytes of payload.</summary>
    private static Guid Compose(DateTimeOffset timestamp, ReadOnlySpan<byte> payload)
    {
        Span<byte> bytes = stackalloc byte[16];

        // 0-5: 48-bit Unix milliseconds, big-endian.
        var milliseconds = timestamp.ToUnixTimeMilliseconds();
        bytes[0] = (byte)(milliseconds >> 40);
        bytes[1] = (byte)(milliseconds >> 32);
        bytes[2] = (byte)(milliseconds >> 24);
        bytes[3] = (byte)(milliseconds >> 16);
        bytes[4] = (byte)(milliseconds >> 8);
        bytes[5] = (byte)milliseconds;

        payload.CopyTo(bytes[6..]);

        // The high 4 bits of byte 6 are the version number (7).
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);

        // The high 2 bits of byte 8 are the RFC 9562 variant (10).
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>Reads the time stamp back out of a UUIDv7.</summary>
    /// <param name="id">The UUIDv7 value.</param>
    /// <returns>The time stamp embedded in the identifier.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is not a version 7 value.</exception>
    public static DateTimeOffset GetTimestamp(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        if (!id.TryWriteBytes(bytes, bigEndian: true, out _))
        {
            throw new ArgumentException("The identifier bytes could not be read.", nameof(id));
        }

        if ((bytes[6] & 0xF0) != 0x70)
        {
            throw new ArgumentException("The identifier is not a UUID version 7 value.", nameof(id));
        }

        long milliseconds = ((long)bytes[0] << 40)
            | ((long)bytes[1] << 32)
            | ((long)bytes[2] << 24)
            | ((long)bytes[3] << 16)
            | ((long)bytes[4] << 8)
            | bytes[5];

        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
    }
}
