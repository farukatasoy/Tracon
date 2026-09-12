namespace Tracon.Core.UnitTests.Audit;

/// <summary>Behavior tests for <see cref="AuditChainHasher"/> (phase 64).</summary>
public sealed class AuditChainHasherTests
{
    private static readonly DateTimeOffset SampleCreatedAt = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Same_inputs_produce_the_same_hash()
    {
        var first = ComputeSample();
        var second = ComputeSample();

        first.ShouldBe(second);
    }

    [Fact]
    public void Hash_is_a_64_character_lowercase_hex_string()
    {
        var hash = ComputeSample();

        hash.Length.ShouldBe(64);
        hash.ShouldBe(hash.ToLowerInvariant());
        hash.ShouldAllBe(static c => Uri.IsHexDigit(c));
    }

    [Fact]
    public void Null_previous_hash_differs_from_a_non_null_one()
    {
        var withoutPrevious = ComputeSample(previousHash: null);
        var withPrevious = ComputeSample(previousHash: new string('0', 64));

        string.Equals(withoutPrevious, withPrevious, StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void Changing_previous_hash_changes_the_result()
    {
        var a = ComputeSample(previousHash: new string('a', 64));
        var b = ComputeSample(previousHash: new string('b', 64));

        string.Equals(a, b, StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void Changing_after_changes_the_result()
    {
        var a = ComputeSample(after: """{"version":1}""");
        var b = ComputeSample(after: """{"version":2}""");

        string.Equals(a, b, StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void Changing_actor_changes_the_result()
    {
        var a = ComputeSample(actor: "alice");
        var b = ComputeSample(actor: "bob");

        string.Equals(a, b, StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void Null_actor_differs_from_the_literal_text_null()
    {
        // 🚨 The canonical form writes an unquoted `null` for a missing field; an
        // actor whose NAME happens to be the text "null" must not collide with it.
        var missingActor = ComputeSample(actor: null);
        var literalActorNamedNull = ComputeSample(actor: "null");

        string.Equals(missingActor, literalActorNamedNull, StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void A_value_that_moves_between_adjacent_fields_does_not_collide()
    {
        // 🚨 Canonical-form field boundary check: without quoting/escaping, moving
        // text across a field boundary could hash identically to a different
        // (entity, before) split of the same characters.
        var moved = AuditChainHasher.ComputeHash(
            previousHash: null,
            tenantId: "tenant-a",
            actor: "user-1",
            action: "agent.update",
            entity: "agent:support:x",
            before: null,
            after: null,
            createdAt: SampleCreatedAt);

        var baseline = AuditChainHasher.ComputeHash(
            previousHash: null,
            tenantId: "tenant-a",
            actor: "user-1",
            action: "agent.update",
            entity: "agent:support",
            before: null,
            after: null,
            createdAt: SampleCreatedAt);

        string.Equals(moved, baseline, StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void Sub_microsecond_ticks_do_not_change_the_hash()
    {
        // 🚨 PostgreSQL's timestamptz stores only microsecond precision; the
        // canonical form rounds createdAt down to microseconds so a value hashed
        // before a PostgreSQL round trip still matches after one (see the type's
        // remarks). Two values that differ only in their last tick digit must
        // therefore hash identically.
        var baseTime = SampleCreatedAt;
        var almostSameTime = baseTime.AddTicks(9);

        var a = ComputeSample(createdAt: baseTime);
        var b = ComputeSample(createdAt: almostSameTime);

        a.ShouldBe(b);
    }

    [Fact]
    public void A_full_microsecond_difference_changes_the_hash()
    {
        var a = ComputeSample(createdAt: SampleCreatedAt);
        var b = ComputeSample(createdAt: SampleCreatedAt.AddTicks(10));

        string.Equals(a, b, StringComparison.Ordinal).ShouldBeFalse();
    }

    private static string ComputeSample(
        string? previousHash = null,
        string tenantId = "tenant-a",
        string? actor = "user-1",
        string action = "agent.update",
        string entity = "agent:support",
        string? before = """{"version":1}""",
        string? after = """{"version":2}""",
        DateTimeOffset? createdAt = null)
        => AuditChainHasher.ComputeHash(
            previousHash,
            tenantId,
            actor,
            action,
            entity,
            before,
            after,
            createdAt ?? SampleCreatedAt);
}
