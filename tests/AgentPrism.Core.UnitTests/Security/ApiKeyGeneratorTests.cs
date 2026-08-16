namespace AgentPrism.Core.UnitTests.Security;

/// <summary>
/// Tests for <see cref="ApiKeyGenerator"/>.
/// </summary>
/// <remarks>
/// Verifies that the key cannot be reversed, and that the hash cannot be
/// predicted (docs/53-KIRACI-API-ANAHTARLARI.md, section 53.2).
/// </remarks>
public sealed class ApiKeyGeneratorTests
{
    [Fact]
    public void Generated_value_carries_the_prefix_and_tenant_segment()
    {
        var generated = ApiKeyGenerator.Generate("acme-corp");

        generated.PlaintextKey.ShouldStartWith("ap_acmecorp_");
        generated.KeyPrefix.ShouldBe(generated.PlaintextKey[..12]);
    }

    [Fact]
    public void Two_generations_do_not_produce_the_same_raw_value()
    {
        var first = ApiKeyGenerator.Generate("tenant");
        var second = ApiKeyGenerator.Generate("tenant");

        string.Equals(first.PlaintextKey, second.PlaintextKey, StringComparison.Ordinal).ShouldBeFalse();
        first.KeyHash.ShouldNotBe(second.KeyHash);
    }

    [Fact]
    public void The_same_raw_value_produces_the_same_hash()
    {
        var generated = ApiKeyGenerator.Generate("tenant");

        var hash1 = ApiKeyGenerator.ComputeHash(generated.PlaintextKey);
        var hash2 = ApiKeyGenerator.ComputeHash(generated.PlaintextKey);

        hash1.ShouldBe(hash2);
        hash1.ShouldBe(generated.KeyHash);
    }

    [Fact]
    public void Hash_is_a_32_byte_SHA256()
        => ApiKeyGenerator.ComputeHash("any-value").Length.ShouldBe(32);

    [Fact]
    public void The_hash_cannot_be_reversed_to_the_raw_value()
    {
        // The hash itself is the output of an irreversible one-way function;
        // what is verified here is that the hash is NOT THE SAME as the raw
        // value -- irreversibility cannot be tested mathematically, but this
        // does catch a regression where the raw value is accidentally stored.
        var generated = ApiKeyGenerator.Generate("tenant");

        var decoded = System.Text.Encoding.UTF8.GetString(generated.KeyHash);
        string.Equals(decoded, generated.PlaintextKey, StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void Special_characters_in_the_tenant_id_are_stripped()
    {
        var generated = ApiKeyGenerator.Generate("Acme.Corp/Prod!!");

        generated.PlaintextKey.ShouldStartWith("ap_acmecorpprod_");
    }

    [Fact]
    public void An_empty_tenant_segment_becomes_default()
    {
        var generated = ApiKeyGenerator.Generate("---");

        generated.PlaintextKey.ShouldStartWith("ap_default_");
    }

    [Fact]
    public void An_empty_tenant_id_is_rejected()
        => Should.Throw<ArgumentException>(() => ApiKeyGenerator.Generate(" "));

    [Fact]
    public void An_empty_raw_value_cannot_be_hashed()
        => Should.Throw<ArgumentException>(() => ApiKeyGenerator.ComputeHash(string.Empty));
}
