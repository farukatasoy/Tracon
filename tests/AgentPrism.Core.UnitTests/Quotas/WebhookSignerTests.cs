namespace AgentPrism.Core.UnitTests.Quotas;

/// <summary>Tests of webhook signing.</summary>
public sealed class WebhookSignerTests
{
    private const string Secret = "s3cret-signing-key";
    private const string Body = """{"event":"run.completed","runId":"019fc0"}""";

    private static readonly DateTimeOffset Timestamp =
        new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Signature_is_produced_with_a_sha256_prefix()
    {
        var signature = WebhookSigner.Sign(Body, Timestamp, Secret);

        signature.ShouldStartWith("sha256=");

        // HMAC-SHA256 = 32 bytes = 64 hex characters.
        signature.Length.ShouldBe(7 + 64);
    }

    [Fact]
    public void Same_input_produces_the_same_signature()
        => WebhookSigner.Sign(Body, Timestamp, Secret)
            .ShouldBe(WebhookSigner.Sign(Body, Timestamp, Secret));

    [Fact]
    public void Correct_signature_verifies()
    {
        var signature = WebhookSigner.Sign(Body, Timestamp, Secret);

        WebhookSigner.Verify(Body, Timestamp, Secret, signature).ShouldBeTrue();
    }

    [Fact]
    public void Signature_changes_when_the_timestamp_changes()
    {
        // 🚨 The timestamp IS included in the signature; without it, a
        // captured request could be replayed forever (K-163).
        var signature = WebhookSigner.Sign(Body, Timestamp, Secret);
        var later = Timestamp.AddSeconds(1);

        string.Equals(WebhookSigner.Sign(Body, later, Secret), signature, StringComparison.Ordinal)
            .ShouldBeFalse();
        WebhookSigner.Verify(Body, later, Secret, signature).ShouldBeFalse();
    }

    [Fact]
    public void Signature_does_not_verify_when_the_body_changes()
    {
        var signature = WebhookSigner.Sign(Body, Timestamp, Secret);

        WebhookSigner.Verify("""{"event":"run.failed"}""", Timestamp, Secret, signature)
            .ShouldBeFalse();
    }

    [Fact]
    public void Wrong_secret_does_not_verify()
    {
        var signature = WebhookSigner.Sign(Body, Timestamp, Secret);

        WebhookSigner.Verify(Body, Timestamp, "wrong-key", signature).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sha256=deadbeef")]
    public void Empty_or_malformed_signature_does_not_verify(string? signature)
        => WebhookSigner.Verify(Body, Timestamp, Secret, signature).ShouldBeFalse();

    [Fact]
    public void Signing_with_an_empty_secret_throws()
        => Should.Throw<ArgumentException>(() => WebhookSigner.Sign(Body, Timestamp, ""));
}
