using System.Net;
using System.Text.Json;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// Whether a failed model call is worth sending again is the decision the model fallback chain makes, and it covers the
/// failures that carry no HTTP status as well as the ones that do.
/// </summary>
/// <remarks>
/// A failure with no status is not always a lost connection: an SDK can refuse an argument or fail to serialize a body
/// before any request leaves, and sending that call again repeats the refusal. Only a failure that got no answer from the
/// provider is transient.
/// </remarks>
public sealed class ProviderInvocationExceptionTransienceTests
{
    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public void A_status_a_later_try_can_change_is_transient(int status)
        => Failure(new HttpRequestException("failed", inner: null, (HttpStatusCode)status)).IsTransient.ShouldBeTrue();

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(422)]
    public void A_permanent_4xx_is_not_transient(int status)
        => Failure(new HttpRequestException("failed", inner: null, (HttpStatusCode)status)).IsTransient.ShouldBeFalse();

    [Fact]
    public void A_request_timeout_status_is_transient()
        => Failure(new HttpRequestException("failed", inner: null, HttpStatusCode.RequestTimeout)).IsTransient.ShouldBeTrue();

    [Fact]
    public void A_connection_that_got_no_answer_is_transient_and_carries_no_status()
    {
        var failure = Failure(new HttpRequestException("Connection refused"));

        failure.StatusCode.ShouldBeNull();
        failure.IsTransient.ShouldBeTrue();
    }

    [Fact]
    public void A_timeout_is_transient()
        => Failure(new TimeoutException("The operation timed out.")).IsTransient.ShouldBeTrue();

    [Fact]
    public void A_failure_before_any_request_was_sent_is_not_transient()
    {
        Failure(new InvalidOperationException("The client refused the call.")).IsTransient.ShouldBeFalse();
        Failure(new JsonException("The body could not be serialized.")).IsTransient.ShouldBeFalse();
    }

    [Fact]
    public void The_decision_is_the_one_the_fallback_chain_makes()
    {
        foreach (var exception in new Exception[]
        {
            new HttpRequestException("failed", inner: null, HttpStatusCode.TooManyRequests),
            new HttpRequestException("failed", inner: null, HttpStatusCode.Unauthorized),
            new HttpRequestException("Connection refused"),
            new InvalidOperationException("refused"),
        })
        {
            Failure(exception).IsTransient.ShouldBe(FallbackRetryClassifier.IsRetryable(exception), exception.Message);
        }
    }

    [Fact]
    public void The_provider_and_the_status_are_carried()
    {
        var failure = Failure(new HttpRequestException("failed", inner: null, HttpStatusCode.ServiceUnavailable));

        failure.Provider.ShouldBe("openai");
        failure.StatusCode.ShouldBe(503);
        failure.ErrorType.ShouldBe(TraconErrorTypes.UpstreamError);
    }

    private static ProviderInvocationException Failure(Exception exception)
        => ProviderInvocationException.UpstreamFailure(exception, "openai", ProviderSecretMask.Empty);
}
