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
        Span<byte> bytes = stackalloc byte[16];

        // 0-5: 48-bit Unix milliseconds, big-endian.
        var milliseconds = timestamp.ToUnixTimeMilliseconds();
        bytes[0] = (byte)(milliseconds >> 40);
        bytes[1] = (byte)(milliseconds >> 32);
        bytes[2] = (byte)(milliseconds >> 24);
        bytes[3] = (byte)(milliseconds >> 16);
        bytes[4] = (byte)(milliseconds >> 8);
        bytes[5] = (byte)milliseconds;

        // 6-15: random.
        RandomNumberGenerator.Fill(bytes[6..]);

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
