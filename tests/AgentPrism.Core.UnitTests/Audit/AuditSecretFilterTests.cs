namespace AgentPrism.Core.UnitTests.Audit;

/// <summary>Behavior tests for the audit trail secret filter.</summary>
public sealed class AuditSecretFilterTests
{
    [Theory]
    [InlineData("apiKey")]
    [InlineData("ApiKey")]
    [InlineData("authorization")]
    [InlineData("Authorization")]
    [InlineData("token")]
    [InlineData("access_token")]
    [InlineData("password")]
    [InlineData("secret")]
    [InlineData("clientSecret")]
    public void Secret_key_is_redacted(string keyName)
    {
        var json = $$"""{"name":"github","{{keyName}}":"very-secret-value"}""";

        var redacted = AuditSecretFilter.Redact(json)!;

        redacted.ShouldContain("\"***\"");
        redacted.ShouldNotContain("very-secret-value");
        redacted.ShouldContain("\"name\":\"github\"");
    }

    [Fact]
    public void Nested_objects_and_arrays_are_also_redacted()
    {
        const string Json = """
            {"name":"support","auth":{"headers":{"Authorization":"Bearer x"}},"items":[{"password":"p1"},{"password":"p2"}]}
            """;

        var redacted = AuditSecretFilter.Redact(Json)!;

        redacted.ShouldNotContain("Bearer x");
        redacted.ShouldNotContain("\"p1\"");
        redacted.ShouldNotContain("\"p2\"");
    }

    [Theory]
    [InlineData("maxOutputTokens")]
    [InlineData("maxContextWindowTokens")]
    [InlineData("totalTokens")]
    [InlineData("inputTokens")]
    [InlineData("outputTokens")]
    public void Plural_token_fields_are_not_treated_as_secrets(string keyName)
    {
        // Measured: in the /agentprism sample app, a real agent.create record had
        // its "maxOutputTokens" field redacted with "***". "token" on its own is
        // a credential; its plural (Tokens) is a count.
        var json = $$"""{"name":"support","{{keyName}}":512}""";

        var redacted = AuditSecretFilter.Redact(json)!;

        redacted.ShouldNotContain("\"***\"");
        redacted.ShouldContain("512");
    }

    [Fact]
    public void Payload_without_secrets_returns_unchanged()
    {
        const string Json = """{"name":"support","version":3}""";

        var redacted = AuditSecretFilter.Redact(Json);

        redacted.ShouldNotBeNull();
        redacted.ShouldContain("\"name\":\"support\"");
        redacted.ShouldContain("\"version\":3");
    }

    [Fact]
    public void Null_is_returned_as_is()
    {
        AuditSecretFilter.Redact(null).ShouldBeNull();
    }

    [Fact]
    public void Invalid_json_is_returned_as_is()
    {
        // For text that may be manually formatted and not valid JSON, such as
        // run_events.payload, the filter silently leaves it unchanged.
        const string NotJson = "orderId=ORD-1";

        AuditSecretFilter.Redact(NotJson).ShouldBe(NotJson);
    }
}
