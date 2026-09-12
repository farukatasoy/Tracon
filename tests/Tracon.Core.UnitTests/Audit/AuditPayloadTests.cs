using System.Text.Json;

namespace Tracon.Core.UnitTests.Audit;

/// <summary>
/// Audit payloads are built with a JSON writer, never by interpolation.
/// </summary>
/// <remarks>
/// 🚨 These tests exist because of a measured defect chain. A value carrying a
/// double quote produced invalid JSON, and three layers then hid the damage:
/// <c>AuditSecretFilter.Redact</c> catches <c>JsonException</c> and returns the
/// text unchanged (so redaction was skipped too), PostgreSQL rejected the
/// <c>jsonb</c> cast with 22P02, and <c>AuditRecorder</c> swallowed the store
/// failure. The mutation had already been applied, so the installation ended up
/// with a tenant-visible change and no audit row - and
/// <c>GET /api/audit/verify</c> could not see it, because a hash chain only
/// links rows that exist.
/// </remarks>
public sealed class AuditPayloadTests
{
    [Theory]
    [InlineData("open\"ai")]
    [InlineData("back\\slash")]
    [InlineData("new\nline")]
    [InlineData("tab\there")]
    public void Array_values_survive_characters_that_used_to_break_the_json(string value)
    {
        var payload = AuditPayload.WriteArray("allowedProviders", [value]);

        // The whole point: this must parse.
        using var document = JsonDocument.Parse(payload);

        document.RootElement
            .GetProperty("allowedProviders")
            .EnumerateArray()
            .Single()
            .GetString()
            .ShouldBe(value);
    }

    [Fact]
    public void A_value_cannot_inject_extra_fields()
    {
        // Interpolation let this value close the string and open a new property.
        var payload = AuditPayload.WriteArray("allowedProviders", ["""x","injected":"yes"""]);

        using var document = JsonDocument.Parse(payload);

        document.RootElement.TryGetProperty("injected", out _)
            .ShouldBeFalse("a value must not be able to add a property to the audit record.");
    }

    [Fact]
    public void Object_writes_the_properties_it_is_given()
    {
        var payload = AuditPayload.Write(writer =>
        {
            writer.WriteString("providerName", "anthropic");
            writer.WriteNumber("version", 3);
            writer.WriteBoolean("enabled", true);
        });

        using var document = JsonDocument.Parse(payload);

        document.RootElement.GetProperty("providerName").GetString().ShouldBe("anthropic");
        document.RootElement.GetProperty("version").GetInt32().ShouldBe(3);
        document.RootElement.GetProperty("enabled").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void Redaction_still_applies_to_what_is_produced()
    {
        // Redact bails out on invalid JSON, so a payload that does not parse also
        // loses its redaction. A payload built here always parses, which is what
        // keeps the secret filter effective.
        var payload = AuditPayload.Write(writer => writer.WriteString("apiKey", "sk-live-secret"));

        AuditSecretFilter.Redact(payload).ShouldNotBeNull().ShouldNotContain("sk-live-secret");
    }
}
