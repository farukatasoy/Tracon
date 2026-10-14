using System.Net;

namespace Tracon.Core.UnitTests.Runs;

/// <summary>
/// Pins which failures <see cref="FinalFailure"/> calls final: the job worker, the
/// workflow node retry and the online evaluation all stop repeating on its answer.
/// </summary>
/// <remarks>
/// The negative rows matter as much as the positive ones. A failure wrongly called
/// final costs a job its retries for a fault the next attempt could clear.
/// </remarks>
public sealed class FinalFailureTests
{
    [Fact]
    public void An_output_cut_off_at_its_limit_is_final()
    {
        FinalFailure.IsFinal(new TraconOutputTruncatedException("cut off")).ShouldBeTrue();
    }

    [Fact]
    public void A_tenant_refusal_is_final_even_when_wrapped()
    {
        var refusal = new TraconTenantProviderRefusedException("Tenant 'acme' is not allowed to call model provider 'openai'.");

        FinalFailure.IsFinal(refusal).ShouldBeTrue();
        FinalFailure.IsFinal(new InvalidOperationException("outer", refusal)).ShouldBeTrue();
        FinalFailure.IsFinal(new AggregateException(new TimeoutException("first"), refusal)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    public void A_normalized_provider_failure_with_a_permanent_4xx_is_final(HttpStatusCode status)
    {
        var failure = ProviderInvocationException.UpstreamFailure(
            new HttpRequestException("Response status code does not indicate success.", inner: null, status),
            "openai");

        FinalFailure.IsFinal(failure).ShouldBeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void A_normalized_provider_failure_the_provider_may_answer_differently_is_not_final(HttpStatusCode status)
    {
        var failure = ProviderInvocationException.UpstreamFailure(
            new HttpRequestException("Response status code does not indicate success.", inner: null, status),
            "openai");

        FinalFailure.IsFinal(failure).ShouldBeFalse();
    }

    /// <summary>
    /// A compilation failure is not final: it wraps a loop criterion factory that
    /// threw and any error the model registry raised while it built a client, and
    /// either can clear on the next attempt.
    /// </summary>
    [Fact]
    public void A_compilation_failure_is_not_final()
    {
        var failure = new TraconCompilationException(
            "Agent 'writer' could not be compiled: loop criterion 'aiJudge' could not be created.",
            new HttpRequestException("connection reset"));

        FinalFailure.IsFinal(failure).ShouldBeFalse();
    }

    /// <summary>
    /// A consumer's own exception keeps its retries, even with a 401 status: only a
    /// failure Tracon normalized at the model-call boundary is read for its status.
    /// </summary>
    [Fact]
    public void A_consumer_exception_that_tracon_did_not_normalize_is_not_final()
    {
        var failure = new HttpRequestException("token endpoint said no", inner: null, HttpStatusCode.Unauthorized);

        FinalFailure.IsFinal(failure).ShouldBeFalse();
    }

    /// <summary>The verdict never reads a message, only stable identities.</summary>
    [Fact]
    public void A_message_that_names_a_final_identity_is_not_final()
    {
        var failure = new InvalidOperationException(
            $"{TraconTenantProviderRefusedException.TenantProviderRefusedErrorType}: HTTP 401 output_truncated");

        FinalFailure.IsFinal(failure).ShouldBeFalse();
    }
}
