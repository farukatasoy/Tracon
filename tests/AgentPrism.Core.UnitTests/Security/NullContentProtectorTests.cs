namespace AgentPrism.Core.UnitTests.Security;

/// <summary>Tests for <see cref="NullContentProtector"/>.</summary>
public sealed class NullContentProtectorTests
{
    [Fact]
    public void IsEnabled_is_always_false()
        => NullContentProtector.Instance.IsEnabled.ShouldBeFalse();

    [Fact]
    public void Protect_writes_plaintext_unchanged()
        => NullContentProtector.Instance.Protect("hello").ShouldBe("hello");

    [Fact]
    public void ProtectBytes_writes_plaintext_unchanged()
    {
        byte[] plaintext = [1, 2, 3];

        NullContentProtector.Instance.ProtectBytes(plaintext).ShouldBe(plaintext);
    }

    [Fact]
    public void Unprotect_returns_a_never_protected_value_unchanged()
        => NullContentProtector.Instance.Unprotect("plain text").ShouldBe("plain text");

    [Fact]
    public void UnprotectBytes_returns_never_protected_bytes_unchanged()
    {
        byte[] plaintext = [4, 5, 6];

        NullContentProtector.Instance.UnprotectBytes(plaintext).ShouldBe(plaintext);
    }

    [Fact]
    public void Unprotect_throws_naming_the_key_id_when_the_value_carries_an_envelope()
    {
        var envelope = ContentProtectionEnvelope.Wrap("k1", new byte[12], [1, 2, 3], new byte[16]);

        Should.Throw<AgentPrismException>(() => NullContentProtector.Instance.Unprotect(envelope)).Message.ShouldContain("k1");
    }

    [Fact]
    public void UnprotectBytes_throws_naming_the_key_id_when_the_value_carries_an_envelope()
    {
        var envelope = ContentProtectionEnvelope.WrapBinary("k2", new byte[12], [1, 2, 3], new byte[16]);

        Should.Throw<AgentPrismException>(() => NullContentProtector.Instance.UnprotectBytes(envelope)).Message.ShouldContain("k2");
    }
}
