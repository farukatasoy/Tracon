using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace AgentPrism;

/// <summary>
/// Derives a deterministic migration lock key from a schema name.
/// </summary>
/// <remarks>
/// The PostgreSQL <c>pg_advisory_lock</c> function takes an arbitrary
/// <see cref="long"/> key; the key is derived from the schema name so that the
/// migration lock is scoped to the schema (K-389). 🚨 <see cref="string.GetHashCode()"/>
/// is <strong>not used</strong>: .NET randomizes that value per process, two
/// replicas compute a different key for the same schema, and the lock silently
/// protects nothing. The same deterministic digest principle that
/// <see cref="MigrationDescriptor"/> uses (SHA-256) is applied instead.
/// </remarks>
internal static class MigrationLockKey
{
    /// <summary>The upper 32 bits of the key; a constant namespace specific to AgentPrism.</summary>
    private const uint NamespaceHigh = 0x41_50_52_49; // "APRI" (ASCII)

    /// <summary>Computes a deterministic 64-bit key from a schema name.</summary>
    /// <param name="schemaName">The validated schema name.</param>
    /// <returns>The same value in every process for the same schema name.</returns>
    public static long ForSchema(string schemaName)
    {
        ArgumentNullException.ThrowIfNull(schemaName);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(schemaName), hash);

        var low32 = BinaryPrimitives.ReadUInt32BigEndian(hash);

        return unchecked((long)(((ulong)NamespaceHigh << 32) | low32));
    }
}
