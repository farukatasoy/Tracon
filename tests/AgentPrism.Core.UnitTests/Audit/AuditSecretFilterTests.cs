namespace AgentPrism.Core.UnitTests.Audit;

/// <summary>Denetim izi sir suzgecinin davranis testleri.</summary>
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
    public void Sir_anahtari_temizlenir(string keyName)
    {
        var json = $$"""{"name":"github","{{keyName}}":"cok-gizli-deger"}""";

        var redacted = AuditSecretFilter.Redact(json)!;

        redacted.ShouldContain("\"***\"");
        redacted.ShouldNotContain("cok-gizli-deger");
        redacted.ShouldContain("\"name\":\"github\"");
    }

    [Fact]
    public void Ic_ice_nesne_ve_dizilerde_de_temizlenir()
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
    public void Cogul_token_alanlari_sir_sayilmaz(string keyName)
    {
        // Olculdu: /agentprism ornek uygulamasinda gercek bir agent.create
        // kaydinda "maxOutputTokens" alani "***" ile gizlenmisti. "token" tek
        // basina bir kimlik dogrulama degeridir; cogulu (Tokens) bir sayimdir.
        var json = $$"""{"name":"support","{{keyName}}":512}""";

        var redacted = AuditSecretFilter.Redact(json)!;

        redacted.ShouldNotContain("\"***\"");
        redacted.ShouldContain("512");
    }

    [Fact]
    public void Sir_icermeyen_yuk_degismeden_doner()
    {
        const string Json = """{"name":"support","version":3}""";

        var redacted = AuditSecretFilter.Redact(Json);

        redacted.ShouldNotBeNull();
        redacted.ShouldContain("\"name\":\"support\"");
        redacted.ShouldContain("\"version\":3");
    }

    [Fact]
    public void Null_oldugu_gibi_doner()
    {
        AuditSecretFilter.Redact(null).ShouldBeNull();
    }

    [Fact]
    public void Gecersiz_json_oldugu_gibi_doner()
    {
        // run_events.payload gibi elle bicimlendirilmis, gecerli JSON olmayabilen
        // metinler icin suzgec sessizce degismeden birakir.
        const string NotJson = "orderId=ORD-1";

        AuditSecretFilter.Redact(NotJson).ShouldBe(NotJson);
    }
}
