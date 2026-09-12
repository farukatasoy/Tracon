namespace Tracon.Core.UnitTests.Audit;

/// <summary>Behavior tests for <see cref="AuditChainWalker"/> (phase 64).</summary>
public sealed class AuditChainWalkerTests
{
    [Fact]
    public void Empty_list_is_valid()
    {
        var result = AuditChainWalker.Verify([]);

        result.Status.ShouldBe(AuditChainStatus.Valid);
        result.EntriesChecked.ShouldBe(0);
        result.FirstFailingEntryId.ShouldBeNull();
    }

    [Fact]
    public void Untouched_chain_is_valid()
    {
        var chain = BuildChain(3);

        var result = AuditChainWalker.Verify(chain);

        result.Status.ShouldBe(AuditChainStatus.Valid);
        result.EntriesChecked.ShouldBe(3);
        result.FirstFailingEntryId.ShouldBeNull();
    }

    [Fact]
    public void Altered_content_is_reported_broken_at_the_altered_entry()
    {
        var chain = BuildChain(3);

        // Simulate an UPDATE that changed `after` without recomputing the hash —
        // exactly what a direct database edit would produce.
        var tampered = chain[1] with { After = """{"version":999}""" };
        chain[1] = tampered;

        var result = AuditChainWalker.Verify(chain);

        result.Status.ShouldBe(AuditChainStatus.Broken);
        result.FirstFailingEntryId.ShouldBe(tampered.Id);
    }

    [Fact]
    public void Altered_previous_hash_is_reported_broken_not_gap()
    {
        // 🚨 Tampering with prev_hash itself is ALSO caught by the self-consistency
        // pass (it changes the row's own recomputed hash) and must be reported as
        // Broken, not Gap — self-consistency runs before link consistency.
        var chain = BuildChain(3);
        var tampered = chain[1] with { PreviousHash = new string('f', 64) };
        chain[1] = tampered;

        var result = AuditChainWalker.Verify(chain);

        result.Status.ShouldBe(AuditChainStatus.Broken);
        result.FirstFailingEntryId.ShouldBe(tampered.Id);
    }

    [Fact]
    public void Deleted_middle_entry_is_reported_as_a_gap()
    {
        var chain = BuildChain(4);
        var withoutThirdEntry = new List<AuditEntry> { chain[0], chain[1], chain[3] };

        var result = AuditChainWalker.Verify(withoutThirdEntry);

        result.Status.ShouldBe(AuditChainStatus.Gap);
        result.FirstFailingEntryId.ShouldBe(chain[3].Id);
    }

    [Fact]
    public void Deleted_first_entry_is_reported_as_a_gap()
    {
        var chain = BuildChain(3);
        var withoutFirstEntry = chain.Skip(1).ToList();

        var result = AuditChainWalker.Verify(withoutFirstEntry);

        result.Status.ShouldBe(AuditChainStatus.Gap);
        result.FirstFailingEntryId.ShouldBe(chain[1].Id);
    }

    [Fact]
    public void Single_surviving_entry_after_deleting_the_rest_is_valid()
    {
        // A lone entry has nothing to link backward to within the walked window;
        // it is judged only on self-consistency, which still holds.
        var chain = BuildChain(3);
        var onlyFirst = new List<AuditEntry> { chain[0] };

        var result = AuditChainWalker.Verify(onlyFirst);

        result.Status.ShouldBe(AuditChainStatus.Valid);
    }

    /// <summary>Builds a valid, correctly linked chain of the given length, as <see cref="AuditChainHasher"/> would.</summary>
    private static List<AuditEntry> BuildChain(int length)
    {
        var chain = new List<AuditEntry>();
        string? previousHash = null;
        var createdAt = new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

        for (var i = 0; i < length; i++)
        {
            var entryCreatedAt = createdAt.AddSeconds(i);
            var hash = AuditChainHasher.ComputeHash(
                previousHash,
                tenantId: "tenant-a",
                actor: "user-1",
                action: "agent.update",
                entity: $"agent:{i}",
                before: null,
                after: $$"""{"version":{{i}}}""",
                entryCreatedAt);

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
            });

            previousHash = hash;
        }

        return chain;
    }
}
