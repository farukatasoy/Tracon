using Microsoft.Extensions.Configuration;

namespace Tracon.Core.UnitTests.Audit;

/// <summary>
/// Behavior tests for <see cref="AuditChainKeyRing"/>: what the host accepts as a key and what
/// it refuses to start with.
/// </summary>
/// <remarks>
/// The message of a refusal names the configuration KEY that is wrong. It never carries a value: the key is a
/// <c>secret</c> and a start failure lands in logs.
/// </remarks>
public sealed class AuditChainKeyRingTests
{
    // A value that is easy to spot in a message if it ever leaks into one. 32 bytes of 0x2A, base64.
    private static readonly string GoodValue = Convert.ToBase64String(Enumerable.Repeat((byte)0x2A, 32).ToArray());

    [Fact]
    public void None_has_no_active_key_and_holds_nothing()
    {
        AuditChainKeyRing.None.Active.ShouldBeNull();
        AuditChainKeyRing.None.TryGet("anything", out _).ShouldBeFalse();
    }

    [Fact]
    public void The_active_key_signs_and_every_key_in_the_section_verifies()
    {
        var ring = AuditChainKeyRing.FromConfiguration("2026-11", Section(("2026-10", GoodValue), ("2026-11", GoodValue)));

        ring.Active.ShouldNotBeNull().Id.ShouldBe("2026-11");
        ring.TryGet("2026-10", out var old).ShouldBeTrue();
        old.Length.ShouldBe(32);
        ring.TryGet("2026-11", out _).ShouldBeTrue();
        ring.TryGet("2026-12", out _).ShouldBeFalse();
    }

    [Fact]
    public void Keys_without_an_active_id_only_verify()
    {
        var ring = AuditChainKeyRing.FromConfiguration(null, Section(("2026-10", GoodValue)));

        ring.Active.ShouldBeNull();
        ring.TryGet("2026-10", out _).ShouldBeTrue();
    }

    [Fact]
    public void An_empty_active_id_means_no_key()
    {
        AuditChainKeyRing.FromConfiguration(string.Empty, Section()).Active.ShouldBeNull();
    }

    [Fact]
    public void No_section_at_all_is_a_ring_with_nothing_in_it()
    {
        var ring = AuditChainKeyRing.FromConfiguration(null, null);

        ring.Active.ShouldBeNull();
        ring.TryGet("x", out _).ShouldBeFalse();
    }

    [Fact]
    public void An_active_id_with_no_value_stops_the_host_and_names_both_keys()
    {
        var failure = Should.Throw<TraconException>(() => AuditChainKeyRing.FromConfiguration("2026-10", Section()));

        failure.Message.ShouldContain("Tracon:Audit:ChainKeyId");
        failure.Message.ShouldContain("Tracon:AuditChainKeys:2026-10");
    }

    [Fact]
    public void A_key_that_is_not_base64_stops_the_host_and_does_not_print_it()
    {
        const string NotBase64 = "this-is-not-base64-!!-and-is-secret-looking";

        var failure = Should.Throw<TraconException>(
            () => AuditChainKeyRing.FromConfiguration("k", Section(("k", NotBase64))));

        failure.Message.ShouldContain("Tracon:AuditChainKeys:k");
        failure.Message.ShouldContain("base64");
        failure.Message.ShouldNotContain(NotBase64);
    }

    [Fact]
    public void A_key_shorter_than_32_bytes_stops_the_host_and_does_not_print_it()
    {
        var shortValue = Convert.ToBase64String(Enumerable.Repeat((byte)0x2A, 31).ToArray());

        var failure = Should.Throw<TraconException>(
            () => AuditChainKeyRing.FromConfiguration("k", Section(("k", shortValue))));

        failure.Message.ShouldContain("Tracon:AuditChainKeys:k");
        failure.Message.ShouldContain("32");
        failure.Message.ShouldNotContain(shortValue);
    }

    [Fact]
    public void A_key_of_exactly_32_bytes_is_accepted()
    {
        AuditChainKeyRing.FromConfiguration("k", Section(("k", GoodValue))).Active.ShouldNotBeNull();
    }

    [Fact]
    public void A_listed_key_with_no_value_stops_the_host_even_when_it_is_not_the_active_one()
    {
        // 🚨 A verify-only key that cannot be read would turn into UnknownKey on the first walk, long after
        // the start; the start is where an operator is looking.
        var failure = Should.Throw<TraconException>(
            () => AuditChainKeyRing.FromConfiguration("good", Section(("good", GoodValue), ("old", string.Empty))));

        failure.Message.ShouldContain("Tracon:AuditChainKeys:old");
    }

    [Theory]
    [InlineData("Key-A")]
    [InlineData("KEY")]
    [InlineData("-leading-dash")]
    [InlineData("has space")]
    [InlineData("has/slash")]
    [InlineData("trailing-newline\n")]
    public void An_id_outside_the_pattern_is_refused_in_the_section(string id)
    {
        var failure = Should.Throw<TraconException>(
            () => AuditChainKeyRing.FromConfiguration(null, Section((id, GoodValue))));

        failure.Message.ShouldContain("Tracon:AuditChainKeys:");
        failure.Message.ShouldContain("lowercase");
    }

    [Fact]
    public void An_upper_case_active_id_is_refused_by_name()
    {
        var failure = Should.Throw<TraconException>(
            () => AuditChainKeyRing.FromConfiguration("Key-A", Section(("key-a", GoodValue))));

        failure.Message.ShouldContain("Tracon:Audit:ChainKeyId");
    }

    [Fact]
    public void An_id_of_64_characters_is_accepted_and_one_of_65_is_not()
    {
        var sixtyFour = new string('a', 64);

        AuditChainKeyRing.FromConfiguration(sixtyFour, Section((sixtyFour, GoodValue))).Active.ShouldNotBeNull();

        Should.Throw<TraconException>(
            () => AuditChainKeyRing.FromConfiguration(null, Section((new string('a', 65), GoodValue))));
    }

    [Fact]
    public void A_section_node_that_is_not_a_leaf_stops_the_host()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["AuditChainKeys:k:nested"] = GoodValue })
            .Build();

        var failure = Should.Throw<TraconException>(
            () => AuditChainKeyRing.FromConfiguration("k", configuration.GetSection("AuditChainKeys")));

        failure.Message.ShouldContain("Tracon:AuditChainKeys:k");
    }

    [Fact]
    public void Create_holds_the_keys_it_is_given_and_refuses_a_short_one()
    {
        var ring = AuditChainKeyRing.Create("a", new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["a"] = new byte[32] });

        ring.Active.ShouldNotBeNull().Id.ShouldBe("a");

        Should.Throw<TraconException>(
            () => AuditChainKeyRing.Create("a", new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["a"] = new byte[31] }));
    }

    [Fact]
    public void A_key_struct_has_no_member_that_prints_the_value()
    {
        var key = new AuditChainKey("k", Enumerable.Repeat((byte)0x2A, 32).ToArray());

        key.ToString().ShouldBe("AuditChainKey k");
    }

    private static IConfiguration Section(params (string Id, string Value)[] keys)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(keys.ToDictionary(
                static key => $"AuditChainKeys:{key.Id}",
                static key => (string?)key.Value,
                StringComparer.Ordinal))
            .Build()
            .GetSection("AuditChainKeys");
}
