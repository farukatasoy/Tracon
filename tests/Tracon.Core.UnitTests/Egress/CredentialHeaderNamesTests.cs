namespace Tracon.Core.UnitTests.Egress;

/// <summary>
/// The credential header classifier (phase 190). The table is the plan's,
/// including the two known gaps: a false positive and the false negatives.
/// </summary>
public sealed class CredentialHeaderNamesTests
{
    [Theory]
    [InlineData("Authorization")]
    [InlineData("Proxy-Authorization")]
    [InlineData("X-API-Key")]
    [InlineData("api_key")]
    [InlineData("Private-Token")]
    [InlineData("X-Auth-Token")]
    [InlineData("Ocp-Apim-Subscription-Key")]
    [InlineData("X-Functions-Key")]
    [InlineData("X-Access-Key")]
    [InlineData("Cookie")]
    [InlineData("cookie")]
    [InlineData("X-Client-Secret")]
    [InlineData("X-Password")]
    public void A_credential_name_is_recognised(string name)
        => CredentialHeaderNames.IsCredential(name).ShouldBeTrue();

    /// <summary>A known false positive: the value moves to configuration, which is harmless.</summary>
    [Fact]
    public void An_idempotency_key_header_is_classified_as_a_credential()
        => CredentialHeaderNames.IsCredential("X-Idempotency-Key").ShouldBeTrue();

    [Theory]
    [InlineData("X-Tenant")]
    [InlineData("X-Server")]
    [InlineData("X-Max-Tokens")]
    [InlineData("Accept")]
    [InlineData("X-Keyboard")]
    public void An_ordinary_name_is_not_a_credential(string name)
        => CredentialHeaderNames.IsCredential(name).ShouldBeFalse();

    /// <summary>
    /// Known false negatives: a name heuristic cannot see them. The mask on
    /// every read and the names-only audit trail cover them.
    /// </summary>
    [Theory]
    [InlineData("X-Auth")]
    [InlineData("X-Signature")]
    [InlineData("X-Session-Id")]
    public void A_known_residual_is_not_caught(string name)
        => CredentialHeaderNames.IsCredential(name).ShouldBeFalse();

    [Theory]
    [InlineData("X-Api-Key", true)]
    [InlineData("x_custom.header~1", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("X Api", false)]
    [InlineData("X-Api:Key", false)]
    [InlineData("X-Api\r\nX-Injected", false)]
    [InlineData("X-Api\n", false)]
    [InlineData("X-Caf\u00E9", false)]
    public void A_header_name_must_be_an_http_token(string? name, bool valid)
        => CredentialHeaderNames.IsValidName(name).ShouldBe(valid);

    [Fact]
    public void Separators_are_stripped_before_matching()
    {
        CredentialHeaderNames.Normalize("x-api-key").ShouldBe("xapikey");
        CredentialHeaderNames.Normalize("API.KEY").ShouldBe("APIKEY");
        CredentialHeaderNames.Normalize("apiKey").ShouldBe("apiKey");
    }
}
