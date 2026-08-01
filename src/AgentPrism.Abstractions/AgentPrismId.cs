using System.Security.Cryptography;

namespace AgentPrism;

/// <summary>
/// AgentPrism kimlikleri icin UUID surum 7 ureteci (RFC 9562).
/// </summary>
/// <remarks>
/// <para>
/// UUIDv7 zaman siralidir: ilk 48 bit Unix milisaniye damgasidir. Bu sayede
/// B-tree index parcalanmasi olusmaz ve <c>ORDER BY id</c> zaman sirasi verir.
/// <c>bigserial</c>'in merkezi sira darbogazi da olusmaz.
/// </para>
/// <para>
/// .NET 9 ile gelen <c>Guid.CreateVersion7()</c> yerine kendi uygulamamizi
/// kullaniyoruz. Sebep: paket <c>net8.0</c> hedefini de destekler ve depolama
/// anahtarlarinin tum hedeflerde ayni sekilde uretilmesi gerekir.
/// </para>
/// </remarks>
public static class AgentPrismId
{
    /// <summary>Su ana damgali yeni bir UUIDv7 uretir.</summary>
    /// <returns>Zaman sirali kimlik.</returns>
    public static Guid NewId() => NewId(DateTimeOffset.UtcNow);

    /// <summary>Belirtilen ana damgali bir UUIDv7 uretir.</summary>
    /// <param name="timestamp">Kimlige gomulecek zaman damgasi.</param>
    /// <returns>Zaman sirali kimlik.</returns>
    public static Guid NewId(DateTimeOffset timestamp)
    {
        Span<byte> bytes = stackalloc byte[16];

        // 0-5: 48 bit Unix milisaniye, big-endian.
        var milliseconds = timestamp.ToUnixTimeMilliseconds();
        bytes[0] = (byte)(milliseconds >> 40);
        bytes[1] = (byte)(milliseconds >> 32);
        bytes[2] = (byte)(milliseconds >> 24);
        bytes[3] = (byte)(milliseconds >> 16);
        bytes[4] = (byte)(milliseconds >> 8);
        bytes[5] = (byte)milliseconds;

        // 6-15: rastgele.
        RandomNumberGenerator.Fill(bytes[6..]);

        // 6. baytin ust 4 biti surum numarasi (7).
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);

        // 8. baytin ust 2 biti RFC 9562 varyanti (10).
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>Bir UUIDv7 icindeki zaman damgasini geri okur.</summary>
    /// <param name="id">UUIDv7 degeri.</param>
    /// <returns>Kimlige gomulu zaman damgasi.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> surum 7 degilse.</exception>
    public static DateTimeOffset GetTimestamp(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        if (!id.TryWriteBytes(bytes, bigEndian: true, out _))
        {
            throw new ArgumentException("Kimlik baytlari okunamadi.", nameof(id));
        }

        if ((bytes[6] & 0xF0) != 0x70)
        {
            throw new ArgumentException("Kimlik bir UUID surum 7 degeri degil.", nameof(id));
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
