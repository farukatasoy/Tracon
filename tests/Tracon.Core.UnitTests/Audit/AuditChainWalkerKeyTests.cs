namespace Tracon.Core.UnitTests.Audit;

/// <summary>
/// Behavior tests for <see cref="AuditChainWalker"/> with signing keys.
/// </summary>
/// <remarks>
/// Every tamper here is what an actor with write access to the table and NO access to the key can do: rewrite
/// content, change <c>key_id</c>, drop it and recompute the plain SHA-256. The walk must refuse each one.
/// </remarks>
public sealed class AuditChainWalkerKeyTests
{
    private static readonly byte[] KeyA = Enumerable.Range(1, 32).Select(static i => (byte)i).ToArray();
    private static readonly byte[] KeyB = Enumerable.Range(101, 32).Select(static i => (byte)i).ToArray();

    [Fact]
    public void A_fully_signed_chain_is_valid_and_counts_no_unkeyed_entries()
    {
        var chain = BuildChain(4, static _ => "a");

        var result = AuditChainWalker.Verify(chain, keys: Ring("a"));

        result.Status.ShouldBe(AuditChainStatus.Valid);
        result.EntriesChecked.ShouldBe(4);
        result.UnkeyedEntries.ShouldBe(0);
        result.FirstFailingEntryId.ShouldBeNull();
    }

    [Fact]
    public void Entries_written_before_the_first_key_stay_valid_and_are_counted()
    {
        var chain = BuildChain(5, static i => i >= 3 ? "a" : null);

        var result = AuditChainWalker.Verify(chain, keys: Ring("a"));

        result.Status.ShouldBe(AuditChainStatus.Valid);
        result.UnkeyedEntries.ShouldBe(3);
    }

    [Fact]
    public void A_chain_with_no_key_at_all_is_valid_for_a_host_with_no_key()
    {
        var chain = BuildChain(3, static _ => null);

        var result = AuditChainWalker.Verify(chain);

        result.Status.ShouldBe(AuditChainStatus.Valid);
        result.UnkeyedEntries.ShouldBe(3);
    }

    [Fact]
    public void An_entry_signed_with_a_key_the_host_does_not_hold_is_unknown_key_not_broken()
    {
        var chain = BuildChain(3, static _ => "a");

        var result = AuditChainWalker.Verify(chain);

        result.Status.ShouldBe(AuditChainStatus.UnknownKey);
        result.FirstFailingEntryId.ShouldBe(chain[0].Id);
    }

    [Fact]
    public void A_tampered_entry_whose_key_id_was_pointed_at_a_key_the_host_does_not_hold_is_unknown_key_and_never_valid()
    {
        // 🚨 An unknown key is not proof of tampering, and that is only half the story: an
        // actor who cannot read the key can change a row's content AND its `key_id` to an id nobody holds, and the walk
        // cannot check that row, so it answers UnknownKey and not Broken. It is never Valid, and it names the row.
        // The wording of the answer must therefore ask for the key history to be compared, not call the row innocent.
        var chain = BuildChain(3, static _ => "a");
        chain[1] = chain[1] with { After = """{"version":999}""", KeyId = "someone-else" };

        var result = AuditChainWalker.Verify(chain, keys: Ring("a"));

        result.Status.ShouldBe(AuditChainStatus.UnknownKey);
        result.Status.ShouldNotBe(AuditChainStatus.Valid);
        result.FirstFailingEntryId.ShouldBe(chain[1].Id);
    }

    [Fact]
    public void A_keyed_entry_changed_in_content_is_broken()
    {
        var chain = BuildChain(3, static _ => "a");
        chain[1] = chain[1] with { After = """{"version":999}""" };

        var result = AuditChainWalker.Verify(chain, keys: Ring("a"));

        result.Status.ShouldBe(AuditChainStatus.Broken);
        result.FirstFailingEntryId.ShouldBe(chain[1].Id);
    }

    [Fact]
    public void A_keyed_entry_rewritten_as_unkeyed_with_a_recomputed_sha256_is_broken_as_a_downgrade()
    {
        // 🚨 The attack the key exists to stop: change the row, drop key_id, recompute the plain hash that
        // needs no key. Row 2's own hash is then consistent; only the order (unsigned after signed) gives it away.
        var chain = BuildChain(4, static _ => "a");
        var victim = chain[2];
        var rewritten = victim with { After = """{"version":666}""", KeyId = null };
        chain[2] = rewritten with
        {
            Hash = AuditChainHasher.ComputeHash(
                rewritten.PreviousHash,
                rewritten.TenantId,
                rewritten.Actor,
                rewritten.Action,
                rewritten.Entity,
                rewritten.Before,
                rewritten.After,
                rewritten.CreatedAt),
        };

        var result = AuditChainWalker.Verify(chain, keys: Ring("a"));

        result.Status.ShouldBe(AuditChainStatus.Broken);
        result.FirstFailingEntryId.ShouldBe(victim.Id);
    }

    [Fact]
    public void A_keyed_entry_recomputed_with_a_plain_sha256_and_the_same_key_id_is_broken()
    {
        // The edit an actor without the key CAN make: new content, same key id, a SHA-256 over the keyed
        // canonical form. Without the key the hash cannot be an HMAC, so the walk refuses it.
        var chain = BuildChain(3, static _ => "a");
        var victim = chain[2];
        var forged = victim with { After = """{"version":666}""" };
        var canonical = AuditChainHasher.BuildCanonicalForm(
            forged.PreviousHash,
            forged.TenantId,
            forged.Actor,
            forged.Action,
            forged.Entity,
            forged.Before,
            forged.After,
            forged.CreatedAt,
            forged.KeyId);
        chain[2] = forged with
        {
            Hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical)))
                .ToLowerInvariant(),
        };

        var result = AuditChainWalker.Verify(chain, keys: Ring("a"));

        result.Status.ShouldBe(AuditChainStatus.Broken);
        result.FirstFailingEntryId.ShouldBe(victim.Id);
    }

    [Fact]
    public void A_keyed_entry_pointed_at_another_known_key_is_broken()
    {
        // The id is inside the signed text, so the label cannot be moved without the key.
        var chain = BuildChain(3, static _ => "a");
        chain[1] = chain[1] with { KeyId = "b" };

        var result = AuditChainWalker.Verify(chain, keys: Ring("a", "b"));

        result.Status.ShouldBe(AuditChainStatus.Broken);
        result.FirstFailingEntryId.ShouldBe(chain[1].Id);
    }

    [Fact]
    public void An_entry_signed_with_the_wrong_key_bytes_under_a_known_id_is_broken()
    {
        var chain = BuildChain(3, static _ => "a");
        var forged = AuditChainHasher.ComputeHash(
            chain[1].PreviousHash,
            chain[1].TenantId,
            chain[1].Actor,
            chain[1].Action,
            chain[1].Entity,
            chain[1].Before,
            chain[1].After,
            chain[1].CreatedAt,
            new AuditChainKey("a", KeyB));
        chain[1] = chain[1] with { Hash = forged };

        var result = AuditChainWalker.Verify(chain, keys: Ring("a"));

        result.Status.ShouldBe(AuditChainStatus.Broken);
        result.FirstFailingEntryId.ShouldBe(chain[1].Id);
    }

    [Fact]
    public void A_chain_signed_by_two_keys_verifies_when_the_host_holds_both()
    {
        var chain = BuildChain(4, static i => i < 2 ? "a" : "b");

        var result = AuditChainWalker.Verify(chain, keys: Ring("b", "a", "b"));

        result.Status.ShouldBe(AuditChainStatus.Valid);
        result.UnkeyedEntries.ShouldBe(0);
    }

    [Fact]
    public void A_removed_old_key_reports_the_first_entry_it_signed()
    {
        var chain = BuildChain(4, static i => i < 2 ? "a" : "b");

        var result = AuditChainWalker.Verify(chain, keys: Ring("b", "b"));

        result.Status.ShouldBe(AuditChainStatus.UnknownKey);
        result.FirstFailingEntryId.ShouldBe(chain[0].Id);
    }

    [Fact]
    public void The_first_failing_row_decides_when_a_broken_row_comes_before_an_unknown_key()
    {
        var chain = BuildChain(3, static i => i == 2 ? "gone" : "a");
        chain[1] = chain[1] with { After = """{"version":999}""" };

        var result = AuditChainWalker.Verify(chain, keys: Ring("a"));

        result.Status.ShouldBe(AuditChainStatus.Broken);
        result.FirstFailingEntryId.ShouldBe(chain[1].Id);
    }

    [Fact]
    public void The_whole_chain_rewritten_as_unkeyed_is_valid_and_the_count_is_the_signal()
    {
        // The named residual risk (docs: Tamper detection). Rewriting EVERY row from the first signed one
        // onward leaves no signed row to be downgraded after, and a plain SHA-256 checks out. The walk
        // cannot call it forged; the unkeyed count equals the row count, which a host signed from its first
        // entry never shows.
        var chain = BuildChain(3, static _ => "a");

        for (var i = 0; i < chain.Count; i++)
        {
            var row = chain[i] with { KeyId = null, PreviousHash = i == 0 ? null : chain[i - 1].Hash };
            chain[i] = row with
            {
                Hash = AuditChainHasher.ComputeHash(
                    row.PreviousHash,
                    row.TenantId,
                    row.Actor,
                    row.Action,
                    row.Entity,
                    row.Before,
                    row.After,
                    row.CreatedAt),
            };
        }

        var result = AuditChainWalker.Verify(chain, keys: Ring("a"));

        result.Status.ShouldBe(AuditChainStatus.Valid);
        result.UnkeyedEntries.ShouldBe(chain.Count);
    }

    [Fact]
    public void A_failed_walk_still_reports_how_many_entries_carry_no_key()
    {
        var chain = BuildChain(5, static i => i >= 2 ? "a" : null);
        chain[4] = chain[4] with { After = """{"version":999}""" };

        var result = AuditChainWalker.Verify(chain, keys: Ring("a"));

        result.Status.ShouldBe(AuditChainStatus.Broken);
        result.UnkeyedEntries.ShouldBe(2);
    }

    [Fact]
    public void A_window_cannot_see_a_signed_entry_before_it()
    {
        // The documented limit of a bounded walk: nothing before the first walked row is read, so an
        // unsigned row at the window's start is judged by its own hash only. The whole-history walk is the one
        // that catches a downgrade.
        var chain = BuildChain(4, static i => i < 2 ? "a" : null);
        var window = chain.Skip(2).ToList();

        var result = AuditChainWalker.Verify(window, hasLowerBound: true, keys: Ring("a"));

        result.Status.ShouldBe(AuditChainStatus.Valid);
    }

    private static AuditChainKeyRing Ring(params string[] ids)
        => AuditChainKeyRing.Create(
            null,
            ids.Distinct(StringComparer.Ordinal).ToDictionary(
                static id => id,
                static id => string.Equals(id, "b", StringComparison.Ordinal) ? KeyB : KeyA,
                StringComparer.Ordinal));

    private static List<AuditEntry> BuildChain(int length, Func<int, string?> keyIdOf)
    {
        var chain = new List<AuditEntry>();
        string? previousHash = null;
        var createdAt = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

        for (var i = 0; i < length; i++)
        {
            var entryCreatedAt = createdAt.AddSeconds(i);
            var keyId = keyIdOf(i);
            AuditChainKey? key = keyId is null
                ? null
                : new AuditChainKey(keyId, string.Equals(keyId, "b", StringComparison.Ordinal) ? KeyB : KeyA);

            var hash = AuditChainHasher.ComputeHash(
                previousHash,
                tenantId: "tenant-a",
                actor: "user-1",
                action: "agent.update",
                entity: $"agent:{i}",
                before: null,
                after: $$"""{"version":{{i}}}""",
                entryCreatedAt,
                key);

            chain.Add(new AuditEntry
            {
                Id = TraconId.NewId(),
                TenantId = "tenant-a",
                Actor = "user-1",
                Action = "agent.update",
                Entity = $"agent:{i}",
                Before = null,
                After = $$"""{"version":{{i}}}""",
                CreatedAt = entryCreatedAt,
                PreviousHash = previousHash,
                Hash = hash,
                KeyId = keyId,
            });

            previousHash = hash;
        }

        return chain;
    }
}
