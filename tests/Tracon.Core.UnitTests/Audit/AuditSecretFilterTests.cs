namespace Tracon.Core.UnitTests.Audit;

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
        // Measured: in the /tracon sample app, a real agent.create record had
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

    /// <summary>
    /// 🚨 Separator spellings used to escape redaction. The fragment list is
    /// written without separators ("apikey"), and matching was a plain substring
    /// test, so "apiKey" was caught while "x-api-key", "xi-api-key" and "api_key"
    /// were not. All three are real spellings in this code base and in the
    /// free-form surfaces a caller controls: MCP server headers, agent metadata
    /// and skill script arguments.
    /// </summary>
    [Theory]
    [InlineData("x-api-key")]
    [InlineData("X-Api-Key")]
    [InlineData("xi-api-key")]
    [InlineData("api_key")]
    [InlineData("API.KEY")]
    [InlineData("client_secret")]
    [InlineData("refresh-token")]
    public void Separated_secret_names_are_redacted(string propertyName)
    {
        var json = $$"""{"{{propertyName}}":"sk-live-secret"}""";

        AuditSecretFilter.Redact(json).ShouldNotBeNull().ShouldNotContain("sk-live-secret");
    }

    /// <summary>
    /// A name that ends in "configurationKey" or "configurationName" holds the
    /// NAME of a configuration entry, never its value (K-059), so redacting it
    /// removes the only useful thing in the record.
    /// </summary>
    [Theory]
    [InlineData("authorizationConfigurationKey")]
    [InlineData("oauthClientSecretConfigurationKey")]
    [InlineData("secretConfigurationKey")]
    [InlineData("apiKeyConfigurationName")]
    [InlineData("signingSecretConfigurationName")]
    public void A_configuration_key_reference_is_kept(string propertyName)
    {
        var json = $$"""{"{{propertyName}}":"Tracon:Providers:OpenAI:ApiKey"}""";

        AuditSecretFilter.Redact(json).ShouldNotBeNull()
            .ShouldContain("Tracon:Providers:OpenAI:ApiKey");
    }

    /// <summary>
    /// A name that ends in "mode" classifies a flow; it never carries a
    /// credential. Measured: an MCP server record redacted
    /// "oauthAuthorizationMode":"AuthorizationCode", so a reader could not see
    /// which OAuth flow the server used.
    /// </summary>
    [Theory]
    [InlineData("oauthAuthorizationMode")]
    [InlineData("tokenMode")]
    public void A_mode_field_is_kept(string propertyName)
    {
        var json = $$"""{"{{propertyName}}":"AuthorizationCode"}""";

        AuditSecretFilter.Redact(json).ShouldNotBeNull().ShouldContain("AuthorizationCode");
    }

    /// <summary>
    /// A value that cannot carry a secret is left alone whatever its name is.
    /// Replacing a null with "***" does not protect anything and tells the
    /// reader a secret is present where none is.
    /// </summary>
    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("false")]
    public void A_value_that_cannot_hold_a_secret_is_kept(string literal)
    {
        var json = $$"""{"authorization":{{literal}}}""";

        AuditSecretFilter.Redact(json).ShouldNotBeNull().ShouldContain(literal);
    }

    /// <summary>A string under the same name is still redacted.</summary>
    [Fact]
    public void A_string_under_an_exempted_shape_is_still_redacted()
    {
        const string Json = """{"authorization":"Bearer sk-live-secret"}""";

        AuditSecretFilter.Redact(Json).ShouldNotBeNull().ShouldNotContain("sk-live-secret");
    }

    /// <summary>
    /// The other half of the contract: stripping separators must not start
    /// redacting counters. K-081 recorded that a plural "tokens" is a count, and
    /// blanket-redacting it emptied real agent records.
    /// </summary>
    [Theory]
    [InlineData("max_output_tokens")]
    [InlineData("max-context-window-tokens")]
    [InlineData("totalTokens")]
    public void Separated_token_counts_are_kept(string propertyName)
    {
        var json = $$"""{"{{propertyName}}":4096}""";

        AuditSecretFilter.Redact(json).ShouldNotBeNull().ShouldContain("4096");
    }

    /// <summary>
    /// A configuration key map holds key NAMES under credential-looking header
    /// names; redacting it would erase which key a header reads (phase 190).
    /// </summary>
    [Fact]
    public void A_configuration_key_map_keeps_its_key_names()
    {
        const string Json = """
            {"name":"m1","headerConfigurationKeys":{"Authorization":"Tracon:McpSecrets:Bearer","X-Api-Key":"Tracon:McpSecrets:SearchKey"},"headers":{"X-Api-Key":"live-value"}}
            """;

        var redacted = AuditSecretFilter.Redact(Json)!;

        redacted.ShouldContain("\"Authorization\":\"Tracon:McpSecrets:Bearer\"");
        redacted.ShouldContain("\"X-Api-Key\":\"Tracon:McpSecrets:SearchKey\"");

        // The plain map next to it is still judged by name.
        redacted.ShouldNotContain("live-value");
    }

    /// <summary>
    /// The exemption is not a name suffix: free-form agent metadata could name
    /// any object "...ConfigurationKeys" and pass a live value through.
    /// </summary>
    [Theory]
    [InlineData("""{"metadata":{"upstreamConfigurationKeys":{"apiKey":"sk-live-190"}}}""")]
    [InlineData("""{"headerConfigurationKeys":{"X-Api-Key":"sk-live-190"}}""")]
    public void Only_the_header_map_with_key_paths_is_exempt(string json)
        => AuditSecretFilter.Redact(json)!.ShouldNotContain("sk-live-190");

    [Fact]
    public void A_nested_value_under_a_configuration_key_map_is_still_judged_by_name()
    {
        // Only a string can be a key name; anything else falls back to the rule.
        const string Json = """
            {"headerConfigurationKeys":{"Authorization":{"password":"nested-secret"}}}
            """;

        AuditSecretFilter.Redact(Json)!.ShouldNotContain("nested-secret");
    }

    /// <summary>
    /// The filter and the header save rule read one list; two lists would drift
    /// and a name one of them catches would reach the other in clear text.
    /// </summary>
    [Theory]
    [InlineData("x-api-key")]
    [InlineData("Private-Token")]
    [InlineData("Proxy-Authorization")]
    [InlineData("client_secret")]
    public void The_filter_and_the_header_rule_agree_on_every_fragment(string name)
    {
        CredentialHeaderNames.IsCredential(name).ShouldBeTrue();
        AuditSecretFilter.Redact($$"""{"{{name}}":"v-190"}""")!.ShouldNotContain("v-190");
    }
}
