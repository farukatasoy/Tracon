namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// The code a provider puts in an error answer is carried as a short token, and nothing else of the answer is.
/// </summary>
/// <remarks>
/// The messages are the ones the OpenAI client library writes (measured against a stub endpoint), and the shapes a code does
/// not come from: a reason phrase, an answer with no body, a sentence put where a code belongs.
/// </remarks>
public sealed class ProviderErrorCodeTests
{
    [Theory]
    [InlineData("HTTP 429 (insufficient_quota: insufficient_quota)\n\nYou exceeded your current quota", "insufficient_quota")]
    [InlineData("HTTP 404 (invalid_request_error: model_not_found)", "model_not_found")]
    [InlineData("HTTP 401 (invalid_api_key)", "invalid_api_key")]
    [InlineData("HTTP 400 (server_error: )\n\nno code", "server_error")]
    [InlineData("HTTP 400 (: content_filter)", "content_filter")]
    [InlineData("HTTP 404 (: DeploymentNotFound)\n\nThe API deployment does not exist", "DeploymentNotFound")]
    public void A_code_the_client_library_wrote_into_its_message_is_read(string message, string expected)
        => ProviderErrorCodes.Read(new InvalidOperationException(message)).ShouldBe(expected);

    [Theory]
    [InlineData("HTTP 503 (Service Unavailable)")]
    [InlineData("Service request failed.\nStatus: 400 (Bad Request)\n")]
    [InlineData("Status Code: NotFound\n{\"type\":\"error\"}")]
    [InlineData("Request failed with status code 404: Not Found")]
    [InlineData("Connection refused (secret-host:443)")]
    public void A_message_with_no_code_in_it_gives_none(string message)
        => ProviderErrorCodes.Read(new InvalidOperationException(message)).ShouldBeNull();

    [Fact]
    public void The_code_is_read_from_the_inner_exception_a_client_library_wraps()
        => ProviderErrorCodes
            .Read(new AggregateException(new InvalidOperationException("outer", new InvalidOperationException("HTTP 429 (rate_limit_exceeded)"))))
            .ShouldBe("rate_limit_exceeded");

    [Fact]
    public void A_code_an_adapter_read_is_attached_to_the_failure_and_read_back()
    {
        var failure = new InvalidOperationException("Service request failed.");

        ProviderErrorCodes.Attach(failure, "content_policy_violation");

        ProviderErrorCodes.Read(failure).ShouldBe("content_policy_violation");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a sentence with spaces")]
    [InlineData("a/b")]
    [InlineData("\"quoted\"")]
    public void A_value_that_is_not_the_shape_of_a_code_is_not_attached(string? value)
    {
        var failure = new InvalidOperationException("x");

        ProviderErrorCodes.Attach(failure, value);

        ProviderErrorCodes.Read(failure).ShouldBeNull();
    }

    [Fact]
    public void A_token_past_sixty_four_characters_is_not_a_code()
    {
        ProviderErrorCodes.IsCode(new string('a', 64)).ShouldBeTrue();
        ProviderErrorCodes.IsCode(new string('a', 65)).ShouldBeFalse();
    }

    [Fact]
    public void A_provider_failure_carries_the_code_and_keeps_the_message_it_always_had()
    {
        var original = new InvalidOperationException("HTTP 429 (insufficient_quota: insufficient_quota)\n\nSecret provider sentence");

        var failure = ProviderInvocationException.UpstreamFailure(original, "openai", ProviderSecretMask.Empty);

        failure.ProviderErrorCode.ShouldBe("insufficient_quota");
        failure.StatusCode.ShouldBe(429);
        failure.Message.ShouldNotContain("Secret provider sentence", Case.Sensitive);
        failure.Message.ShouldNotContain("insufficient_quota", Case.Sensitive);
    }
}
