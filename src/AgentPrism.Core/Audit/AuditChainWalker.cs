namespace AgentPrism;

/// <summary>
/// Walks an ordered list of audit entries and reports the chain status (phase 64).
/// Shared by <see cref="InMemoryAuditLog"/> and the SQL providers so the two
/// verify algorithms can never drift apart.
/// </summary>
public static class AuditChainWalker
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
    /// <returns>The verification result.</returns>
    /// <remarks>
    /// <para>
    /// Three checks, in this order:
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///     <strong>Self-consistency.</strong> Each entry's stored <see cref="AuditEntry.Hash"/> is
    ///     recomputed from its own content and its own stored
    ///     <see cref="AuditEntry.PreviousHash"/>. A mismatch means the row was altered
    ///     (any field, including <c>PreviousHash</c> itself) after it was written —
    ///     <see cref="AuditChainStatus.Broken"/>. This pass runs first and wins over
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
    public static AuditChainVerification Verify(IReadOnlyList<AuditEntry> entries, bool hasLowerBound = false)
    {
        ArgumentNullException.ThrowIfNull(entries);

        foreach (var entry in entries)
        {
            var expectedHash = AuditChainHasher.ComputeHash(
                entry.PreviousHash,
                entry.TenantId,
                entry.Actor,
                entry.Action,
                entry.Entity,
                entry.Before,
                entry.After,
                entry.CreatedAt);

            if (!string.Equals(expectedHash, entry.Hash, StringComparison.Ordinal))
            {
                return new AuditChainVerification
                {
                    Status = AuditChainStatus.Broken,
                    EntriesChecked = entries.Count,
                    FirstFailingEntryId = entry.Id,
                };
            }
        }

        if (!hasLowerBound && entries.Count > 0 && entries[0].PreviousHash is not null)
        {
            return new AuditChainVerification
            {
                Status = AuditChainStatus.Gap,
                EntriesChecked = entries.Count,
                FirstFailingEntryId = entries[0].Id,
            };
        }

        for (var i = 1; i < entries.Count; i++)
        {
            if (!string.Equals(entries[i].PreviousHash, entries[i - 1].Hash, StringComparison.Ordinal))
            {
                return new AuditChainVerification
                {
                    Status = AuditChainStatus.Gap,
                    EntriesChecked = entries.Count,
                    FirstFailingEntryId = entries[i].Id,
                };
            }
        }

        return new AuditChainVerification { Status = AuditChainStatus.Valid, EntriesChecked = entries.Count };
    }
}
