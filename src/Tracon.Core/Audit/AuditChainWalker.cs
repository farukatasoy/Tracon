using System.Security.Cryptography;
using System.Text;

namespace Tracon;

/// <summary>
/// Walks an ordered list of audit entries and reports the chain status.
/// Shared by <see cref="InMemoryAuditLog"/> and the SQL providers so the two
/// verify algorithms can never drift apart.
/// </summary>
internal static class AuditChainWalker
{
    /// <summary>Verifies an ordered (oldest first) list of entries belonging to one tenant.</summary>
    /// <param name="entries">The entries, ordered oldest to newest (<c>created_at</c>, then <c>id</c>).</param>
    /// <param name="hasLowerBound">
    /// <see langword="true"/> when the query that produced <paramref name="entries"/> had a lower
    /// date bound (<see cref="AuditChainQuery.After"/> was set) — meaning an entry may exist
    /// before <paramref name="entries"/>[0] that this call cannot see, so nothing can be
    /// concluded about the very first entry's <see cref="AuditEntry.PreviousHash"/>.
    /// <see langword="false"/> (the default) means the query covered the tenant's whole
    /// history, so <paramref name="entries"/>[0] IS the tenant's genesis entry and its
    /// <see cref="AuditEntry.PreviousHash"/> must be <see langword="null"/>.
    /// </param>
    /// <param name="keys">
    /// The keys the host holds, or <see langword="null"/> for none. An entry that names a key is
    /// checked with that key; an entry that names none is checked with a plain SHA-256.
    /// </param>
    /// <returns>The verification result.</returns>
    /// <remarks>
    /// <para>
    /// Three checks, in this order:
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///     <strong>Self-consistency.</strong> Each entry's stored <see cref="AuditEntry.Hash"/> is
    ///     recomputed from its own content and its own stored
    ///     <see cref="AuditEntry.PreviousHash"/>, with the key the entry names (a plain SHA-256 when it
    ///     names none). A mismatch means the row was altered
    ///     (any field, including <c>PreviousHash</c> itself and the key id) after it was written —
    ///     <see cref="AuditChainStatus.Broken"/>. An entry that names a key the ring does not hold is
    ///     <see cref="AuditChainStatus.UnknownKey"/>, and an entry without a key that follows an entry with
    ///     one is <see cref="AuditChainStatus.Broken"/> (a downgrade). The first failing row decides.
    ///     This pass runs first and wins over
    ///     the next two: an altered row also breaks the link to its neighbor, but
    ///     "the row was altered" is the more specific and more useful diagnosis.
    ///   </description></item>
    ///   <item><description>
    ///     <strong>Genesis check.</strong> Only when <paramref name="hasLowerBound"/> is
    ///     <see langword="false"/>: <paramref name="entries"/>[0] must have a
    ///     <see langword="null"/> <see cref="AuditEntry.PreviousHash"/>. A non-null value here
    ///     means the tenant's true first entry (or entries right after it) were deleted —
    ///     also <see cref="AuditChainStatus.Gap"/>. Without this check, deleting a chain's
    ///     OLDEST row is invisible: every SURVIVING row is still internally
    ///     self-consistent and still correctly linked to ITS OWN surviving neighbor.
    ///   </description></item>
    ///   <item><description>
    ///     <strong>Link consistency.</strong> Only once every entry is
    ///     self-consistent: entry <c>i</c>'s <see cref="AuditEntry.PreviousHash"/>
    ///     must equal entry <c>i-1</c>'s <see cref="AuditEntry.Hash"/>. A mismatch
    ///     means a row between them is missing — <see cref="AuditChainStatus.Gap"/>.
    ///   </description></item>
    /// </list>
    /// </remarks>
    public static AuditChainVerification Verify(
        IReadOnlyList<AuditEntry> entries,
        bool hasLowerBound = false,
        AuditChainKeyRing? keys = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        keys ??= AuditChainKeyRing.None;

        var unkeyed = 0;

        foreach (var entry in entries)
        {
            if (entry.KeyId is null)
            {
                unkeyed++;
            }
        }

        var seenKeyed = false;

        foreach (var entry in entries)
        {
            AuditChainKey? key = null;

            if (entry.KeyId is { } keyId)
            {
                // A key the ring does not hold: the walk cannot check the row, and says so with its own
                // status instead of "Broken". The row may be innocent (the key was removed) or may have had its
                // key id changed by hand; the walk cannot tell which, and the answer is never Valid.
                if (!keys.TryGet(keyId, out var value))
                {
                    return Failure(AuditChainStatus.UnknownKey, entries.Count, entry.Id, unkeyed);
                }

                key = new AuditChainKey(keyId, value);
            }
            else if (seenKeyed)
            {
                // 🚨 Downgrade. An unsigned row after a signed one is what an actor sees as the cheap way
                // out: rewrite the row, drop `key_id`, recompute a plain SHA-256 that needs no key. The
                // chain starts unsigned and only ever turns signed, so this order cannot be honest.
                return Failure(AuditChainStatus.Broken, entries.Count, entry.Id, unkeyed);
            }

            var expectedHash = AuditChainHasher.ComputeHash(
                entry.PreviousHash,
                entry.TenantId,
                entry.Actor,
                entry.Action,
                entry.Entity,
                entry.Before,
                entry.After,
                entry.CreatedAt,
                key);

            // The keyed hash is a message authentication code: compared in constant time, so a caller who can
            // read the answer of the verify endpoint learns nothing from how long the comparison took (F-763).
            if (!HashEquals(expectedHash, entry.Hash))
            {
                return Failure(AuditChainStatus.Broken, entries.Count, entry.Id, unkeyed);
            }

            seenKeyed |= key is not null;
        }

        if (!hasLowerBound && entries.Count > 0 && entries[0].PreviousHash is not null)
        {
            return Failure(AuditChainStatus.Gap, entries.Count, entries[0].Id, unkeyed);
        }

        for (var i = 1; i < entries.Count; i++)
        {
            if (!string.Equals(entries[i].PreviousHash, entries[i - 1].Hash, StringComparison.Ordinal))
            {
                return Failure(AuditChainStatus.Gap, entries.Count, entries[i].Id, unkeyed);
            }
        }

        return new AuditChainVerification
        {
            Status = AuditChainStatus.Valid,
            EntriesChecked = entries.Count,
            UnkeyedEntries = unkeyed,
        };
    }

    private static bool HashEquals(string expected, string? actual)
        => actual is not null
           && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));

    private static AuditChainVerification Failure(AuditChainStatus status, int entriesChecked, Guid firstFailingEntryId, int unkeyed)
        => new()
        {
            Status = status,
            EntriesChecked = entriesChecked,
            FirstFailingEntryId = firstFailingEntryId,
            UnkeyedEntries = unkeyed,
        };
}
