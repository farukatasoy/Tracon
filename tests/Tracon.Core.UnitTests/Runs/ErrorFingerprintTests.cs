namespace Tracon.Core.UnitTests.Runs;

/// <summary>
/// Verifies <see cref="ErrorFingerprint"/>'s normalization steps and
/// clustering stability.
/// </summary>
public sealed class ErrorFingerprintTests
{
    [Fact]
    public void Two_similar_messages_containing_a_guid_produce_the_same_fingerprint()
    {
        var first = ErrorFingerprint.Compute("run 3f2a1c4e-8b9d-4e11-9a2f-7c6d5e4f3a2b not found");
        var second = ErrorFingerprint.Compute("run 9d8e7f6a-1b2c-4d3e-8f9a-0b1c2d3e4f5a not found");

        first.ShouldBe(second);
    }

    [Fact]
    public void Two_similar_messages_containing_a_number_produce_the_same_fingerprint()
    {
        var first = ErrorFingerprint.Compute("call timed out after 4823 ms");
        var second = ErrorFingerprint.Compute("call timed out after 91 ms");

        first.ShouldBe(second);
    }

    [Fact]
    public void Two_similar_messages_containing_a_date_and_time_produce_the_same_fingerprint()
    {
        var first = ErrorFingerprint.Compute("request started at 2026-08-06T10:15:30Z and failed");
        var second = ErrorFingerprint.Compute("request started at 2019-01-02T03:04:05Z and failed");

        first.ShouldBe(second);
    }

    [Fact]
    public void Different_failures_produce_different_fingerprints()
    {
        var timeout = ErrorFingerprint.Compute("operation timed out");
        var refused = ErrorFingerprint.Compute("connection refused");

        string.Equals(timeout, refused, StringComparison.Ordinal).ShouldBeFalse();
    }

    /// <summary>
    /// Open Question 3 -> C: quoted text is DELIBERATELY not stripped. The
    /// tool name is a distinguishing factor; two different tools' errors
    /// must NOT be grouped into the same cluster.
    /// </summary>
    [Fact]
    public void Messages_naming_different_tools_stay_in_separate_clusters()
    {
        var refundFailure = ErrorFingerprint.Compute("Tool 'refund_order' failed");
        var shipmentFailure = ErrorFingerprint.Compute("Tool 'track_shipment' failed");

        string.Equals(refundFailure, shipmentFailure, StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void Same_message_always_produces_the_same_fingerprint()
    {
        const string message = "Provider returned 429 Too Many Requests, run 3f2a1c4e-8b9d-4e11-9a2f-7c6d5e4f3a2b";

        var first = ErrorFingerprint.Compute(message);
        var second = ErrorFingerprint.Compute(message);
        var third = ErrorFingerprint.Compute(message);

        first.ShouldBe(second);
        second.ShouldBe(third);
    }

    [Fact]
    public void Fingerprint_is_a_lowercase_hex_sha256_digest()
    {
        var fingerprint = ErrorFingerprint.Compute("a simple error");

        fingerprint.Length.ShouldBe(64);
        fingerprint.ShouldBe(fingerprint.ToLowerInvariant());
    }
}
