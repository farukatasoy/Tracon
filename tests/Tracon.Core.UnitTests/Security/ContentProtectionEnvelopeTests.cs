using System.Text;

namespace Tracon.Core.UnitTests.Security;

/// <summary>Tests for <see cref="ContentProtectionEnvelope"/>.</summary>
public sealed class ContentProtectionEnvelopeTests
{
    [Fact]
    public void Wrap_then_unwrap_round_trips_key_id_nonce_ciphertext_and_tag()
    {
        var nonce = Enumerable.Range(0, 12).Select(static i => (byte)i).ToArray();
        var ciphertext = Encoding.UTF8.GetBytes("cipher-bytes");
        var tag = Enumerable.Range(0, 16).Select(static i => (byte)(i + 100)).ToArray();

        var envelope = ContentProtectionEnvelope.Wrap("k1", nonce, ciphertext, tag);

        ContentProtectionEnvelope.TryUnwrap(envelope, out var keyId, out var unwrappedNonce, out var unwrappedCiphertext, out var unwrappedTag)
            .ShouldBeTrue();
        keyId.ShouldBe("k1");
        unwrappedNonce.ShouldBe(nonce);
        unwrappedCiphertext.ShouldBe(ciphertext);
        unwrappedTag.ShouldBe(tag);
    }

    [Fact]
    public void Wrap_produces_valid_json()
    {
        var envelope = ContentProtectionEnvelope.Wrap("k1", new byte[12], [1, 2, 3], new byte[16]);

        Should.NotThrow(() => System.Text.Json.JsonDocument.Parse(envelope));
    }

    [Theory]
    [InlineData("plain text")]
    [InlineData("{\"role\":\"user\",\"content\":\"hi\"}")]
    [InlineData("")]
    public void TryUnwrap_returns_false_for_a_value_that_was_never_protected(string plaintext)
    {
        ContentProtectionEnvelope.TryUnwrap(plaintext, out var keyId, out _, out _, out _).ShouldBeFalse();
        keyId.ShouldBeNull();
    }

    [Fact]
    public void TryUnwrap_throws_when_the_envelope_carries_no_key_id()
    {
        const string malformed = "{\"$apEnc\":1,\"n\":\"AAAAAAAAAAAAAAAA\",\"c\":\"AAAAAAAAAAAAAAAA\"}";

        Should.Throw<TraconException>(() => ContentProtectionEnvelope.TryUnwrap(malformed, out _, out _, out _, out _));
    }

    [Fact]
    public void TryUnwrap_throws_when_the_ciphertext_is_shorter_than_the_authentication_tag()
    {
        var envelope = ContentProtectionEnvelope.Wrap("k1", new byte[12], [], new byte[4]);

        Should.Throw<TraconException>(() => ContentProtectionEnvelope.TryUnwrap(envelope, out _, out _, out _, out _));
    }

    [Fact]
    public void WrapBinary_then_unwrap_round_trips_key_id_nonce_ciphertext_and_tag()
    {
        var nonce = Enumerable.Range(0, 12).Select(static i => (byte)i).ToArray();
        var ciphertext = Encoding.UTF8.GetBytes("binary-cipher");
        var tag = Enumerable.Range(0, 16).Select(static i => (byte)(i + 50)).ToArray();

        var envelope = ContentProtectionEnvelope.WrapBinary("k2", nonce, ciphertext, tag);

        ContentProtectionEnvelope.TryUnwrapBinary(envelope, out var keyId, out var unwrappedNonce, out var unwrappedCiphertext, out var unwrappedTag)
            .ShouldBeTrue();
        keyId.ShouldBe("k2");
        unwrappedNonce.ShouldBe(nonce);
        unwrappedCiphertext.ShouldBe(ciphertext);
        unwrappedTag.ShouldBe(tag);
    }

    [Fact]
    public void TryUnwrapBinary_returns_false_for_bytes_that_were_never_protected()
    {
        var plaintext = Encoding.UTF8.GetBytes("just some file content");

        ContentProtectionEnvelope.TryUnwrapBinary(plaintext, out var keyId, out _, out _, out _).ShouldBeFalse();
        keyId.ShouldBeNull();
    }

    [Fact]
    public void TryUnwrapBinary_returns_false_for_bytes_shorter_than_the_magic_number()
    {
        ContentProtectionEnvelope.TryUnwrapBinary([1, 2], out _, out _, out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void WrapBinary_rejects_a_key_id_too_long_for_the_one_byte_header_field()
    {
        var tooLong = new string('k', 256);

        Should.Throw<TraconException>(() => ContentProtectionEnvelope.WrapBinary(tooLong, new byte[12], [], new byte[16]));
    }
}
