using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace AgentPrism;

/// <summary>
/// Sema adindan deterministik bir migration kilit anahtari uretir.
/// </summary>
/// <remarks>
/// PostgreSQL'in <c>pg_advisory_lock</c> islevi keyfi bir <see cref="long"/>
/// anahtar alir; migration kilidi semaya kapsanmasi icin (K-389) bu anahtar
/// sema adindan turetilir. 🚨 <see cref="string.GetHashCode()"/>
/// <strong>kullanilmaz</strong>: .NET bu degeri surec basina rastgeler, iki
/// replika ayni sema icin farkli anahtar hesaplar ve kilit sessizce hicbir
/// sey korumaz olur. Bunun yerine <see cref="MigrationDescriptor"/>'in
/// kullandigi SHA-256 ile ayni deterministik ozet ilkesi uygulanir.
/// </remarks>
internal static class MigrationLockKey
{
    /// <summary>Anahtarin ust 32 biti; AgentPrism'e ozgu sabit bir ad alani.</summary>
    private const uint NamespaceHigh = 0x41_50_52_49; // "APRI" (ASCII)

    /// <summary>Sema adindan deterministik bir 64 bit anahtar hesaplar.</summary>
    /// <param name="schemaName">Dogrulanmis sema adi.</param>
    /// <returns>Ayni sema adi icin her surecte ayni deger.</returns>
    public static long ForSchema(string schemaName)
    {
        ArgumentNullException.ThrowIfNull(schemaName);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(schemaName), hash);

        var low32 = BinaryPrimitives.ReadUInt32BigEndian(hash);

        return unchecked((long)(((ulong)NamespaceHigh << 32) | low32));
    }
}
