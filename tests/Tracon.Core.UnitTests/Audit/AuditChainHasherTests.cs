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

    // --- Golden vectors (phase 260) ---
    // 🚨 These two hashes were computed BEFORE the keyed form existed, by an independent
    // implementation of the canonical form (Python hashlib over the same text), and checked against
    // the code of phase 259. An entry written without a key must hash to exactly these values
    // for as long as 1.x lasts: every chain a deployment has written is verified against them.

    [Fact]
    public void An_unkeyed_entry_hashes_to_the_value_phase_259_wrote()
    {
        const string Expected = "c043d7125e46c067498c9377a77ed5189f9f8a957b09814dfb285ee52fa7e5e9";

        var hash = AuditChainHasher.ComputeHash(
            previousHash: "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            tenantId: "acme",
            actor: "user-1",
            action: "agent.update",
            entity: "agent:support",
            before: "{\"version\":1}",
            after: "{\"version\":2,\"note\":\"line\nbreak \\\"q\\\" back\\\\slash\"}",
            createdAt: new DateTimeOffset(2026, 10, 29, 12, 34, 56, TimeSpan.Zero).AddTicks(1234567));

        hash.ShouldBe(Expected);
    }

    [Fact]
    public void An_unkeyed_genesis_entry_with_null_fields_hashes_to_the_value_phase_259_wrote()
    {
        const string Expected = "6f103deeb516efe08ca462704e64788d73f9b8228ea6209d5b46ab5d7da32803";

        var hash = AuditChainHasher.ComputeHash(
            previousHash: null,
            tenantId: "default",
            actor: null,
            action: "agent.create",
            entity: "agent:x",
            before: null,
            after: null,
            createdAt: new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

        hash.ShouldBe(Expected);
    }

    // --- Keyed form (phase 260) ---

    private static readonly byte[] KeyBytes = Enumerable.Range(1, 32).Select(static i => (byte)i).ToArray();

    [Fact]
    public void A_keyed_entry_hashes_to_an_hmac_over_the_canonical_form_with_the_key_id_appended()
    {
        // Computed by an independent implementation (Python hmac over the same text), not by this code.
        const string Expected = "62f14ea0c0c17e1c7288cc8be22d708630d50ea5e4d21fc9bac081495c6e1533";

        var hash = AuditChainHasher.ComputeHash(
            previousHash: "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            tenantId: "acme",
            actor: "user-1",
            action: "agent.update",
            entity: "agent:support",
            before: "{\"version\":1}",
            after: "{\"version\":2}",
            createdAt: new DateTimeOffset(2026, 10, 29, 12, 34, 56, TimeSpan.Zero).AddTicks(1234567),
            key: new AuditChainKey("2026-10", KeyBytes));

        hash.ShouldBe(Expected);
    }

    [Fact]
    public void The_keyed_canonical_form_ends_with_the_key_id_and_the_unkeyed_form_does_not_carry_one()
    {
        var keyed = AuditChainHasher.BuildCanonicalForm(null, "t", null, "a", "e", null, null, SampleCreatedAt, "2026-10");
        var unkeyed = AuditChainHasher.BuildCanonicalForm(null, "t", null, "a", "e", null, null, SampleCreatedAt);

        keyed.ShouldEndWith(",\"keyId\":\"2026-10\"}");
        unkeyed.ShouldNotContain("keyId");
        keyed[..^",\"keyId\":\"2026-10\"}".Length].ShouldBe(unkeyed[..^1]);
    }

    [Fact]
    public void The_same_key_bytes_under_another_key_id_give_another_hash()
    {
        // 🚨 The id is inside the signed text: re-pointing a stored row at a second known key must break it.
        var first = ComputeSample(key: new AuditChainKey("2026-10", KeyBytes));
        var second = ComputeSample(key: new AuditChainKey("2026-11", KeyBytes));

        string.Equals(first, second, StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void Another_key_gives_another_hash()
    {
        var other = Enumerable.Range(2, 32).Select(static i => (byte)i).ToArray();

        var first = ComputeSample(key: new AuditChainKey("2026-10", KeyBytes));
        var second = ComputeSample(key: new AuditChainKey("2026-10", other));

        string.Equals(first, second, StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void A_keyed_hash_is_not_the_plain_sha256_of_any_form_an_unkeyed_writer_can_compute()
    {
        var keyed = ComputeSample(key: new AuditChainKey("2026-10", KeyBytes));
        var unkeyed = ComputeSample();

        string.Equals(keyed, unkeyed, StringComparison.Ordinal).ShouldBeFalse();
        keyed.Length.ShouldBe(64);
        keyed.ShouldAllBe(static c => Uri.IsHexDigit(c) && !char.IsUpper(c));
    }

    private static string ComputeSample(
        AuditChainKey? key,
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
            createdAt ?? SampleCreatedAt,
            key);

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
