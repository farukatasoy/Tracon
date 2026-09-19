namespace Tracon.Core.UnitTests.Runs;

/// <summary>
/// Verifies <see cref="DefaultRunErrorClassifier"/>'s rule matches, and that
/// the old and new <c>error_type</c> formats land in the same class.
/// </summary>
public sealed class DefaultRunErrorClassifierTests
{
    private readonly DefaultRunErrorClassifier _classifier = new();

    [Theory]
    [InlineData("content_filtered", "response was filtered", RunErrorClass.ContentFiltered)]
    [InlineData("compilation_failed", "agent could not be compiled", RunErrorClass.CompilationFailed)]
    [InlineData("provider_unavailable", "circuit breaker is open", RunErrorClass.ProviderUnavailable)]
    [InlineData("System.Net.Http.HttpRequestException", "connection reset", RunErrorClass.ProviderError)]
    // Measured against a real OpenAI 404 response (samples/Tracon.Api): the
    // official SDK throws ClientResultException, not HttpRequestException.
    [InlineData("System.ClientModel.ClientResultException", "HTTP 404 (invalid_request_error: model_not_found)\n\nThe model `gpt-x` does not exist or you do not have access to it.", RunErrorClass.ProviderError)]
    [InlineData("System.Exception", "server returned HTTP 503", RunErrorClass.ProviderError)]
    [InlineData("System.Exception", "The server returned 429 Too Many Requests", RunErrorClass.RateLimited)]
    [InlineData("System.Exception", "rate limit exceeded, retry later", RunErrorClass.RateLimited)]
    [InlineData("System.Exception", "The quota defined for this tenant was exceeded.", RunErrorClass.QuotaExceeded)]
    [InlineData("System.Exception", "An error occurred while running tool 'refund_order'", RunErrorClass.ToolError)]
    [InlineData("System.TimeoutException", "the operation timed out", RunErrorClass.Timeout)]
    [InlineData("System.Exception", "the operation has timed out", RunErrorClass.Timeout)]
    [InlineData("System.OperationCanceledException", "canceled", RunErrorClass.Canceled)]
    [InlineData("System.Threading.Tasks.TaskCanceledException", "canceled", RunErrorClass.Canceled)]
    // 🚨 Phase 157 (K-737): the SAME type, classified differently by its
    // message. TaskCanceledException is what HttpClient raises on ITS OWN
    // request timeout, so the type alone cannot separate "the caller pressed
    // stop" from "the provider never answered". These two rows pin the ORDER
    // of the checks - move the timeout check back below the cancelled-type
    // check and the first one goes red, which is the whole point: before the
    // fix, every provider timeout was filed as Canceled and vanished from the
    // failure numbers.
    [InlineData("System.Threading.Tasks.TaskCanceledException", "The request to https://api.example/v1 timed out after 30s.", RunErrorClass.Timeout)]
    [InlineData("System.OperationCanceledException", "A task was canceled.", RunErrorClass.Canceled)]
    // Phase 69: a tool timeout must NOT fall into Canceled (its wrapper races
    // the call using its own linked cancellation, which looks identical to a
    // user cancellation at the exception-type level) nor into the run-level
    // Timeout class (that one means the WHOLE run exceeded its limit).
    [InlineData("tool_timeout", "Tool 'slow_tool' did not complete within 1s.", RunErrorClass.ToolTimeout)]
    // Phase 114: mapped by STABLE IDENTITY, not the "quota" keyword pattern —
    // the message below deliberately carries neither "quota" nor "kota" to
    // prove the mapping does not depend on the regex.
    [InlineData("run_budget_exceeded", "The run tree's token budget is exhausted (200000/200000). No further model calls can be made in this run tree.", RunErrorClass.QuotaExceeded)]
    public void Every_class_lands_in_the_right_bucket_with_at_least_one_example(string type, string message, RunErrorClass expected)
    {
        var result = _classifier.Classify(new RunError { Type = type, Message = message });

        result.Class.ShouldBe(expected);
    }

    [Theory]
    [InlineData("App.Specific.UnexpectedType", "matches no rule")]
    [InlineData("System.Exception", "a generic error message that contains no keywords")]
    public void Unrecognized_error_is_not_guessed_it_becomes_unknown(string type, string message)
    {
        var result = _classifier.Classify(new RunError { Type = type, Message = message });

        result.Class.ShouldBe(RunErrorClass.Unknown);
    }

    [Theory]
    [InlineData("compilation_failed", "Tracon.TraconCompilationException", RunErrorClass.CompilationFailed)]
    [InlineData("provider_unavailable", "Tracon.TraconProviderUnavailableException", RunErrorClass.ProviderUnavailable)]
    public void Old_and_new_error_type_formats_map_to_the_same_class(string stableType, string legacyType, RunErrorClass expected)
    {
        var stable = _classifier.Classify(new RunError { Type = stableType, Message = "error" });
        var legacy = _classifier.Classify(new RunError { Type = legacyType, Message = "error" });

        stable.Class.ShouldBe(expected);
        legacy.Class.ShouldBe(expected);
    }

    /// <summary>
    /// A provider timeout that crossed the model-call boundary still lands in
    /// <see cref="RunErrorClass.Timeout"/>, not in the broader
    /// <see cref="RunErrorClass.ProviderError"/>.
    /// </summary>
    /// <remarks>
    /// 🚨 Measured live (MT-RES-089, 2026-09-19) against an unroutable
    /// endpoint, and the FIRST run to reach here in this shape: the real SDK
    /// wraps its timeout, so <c>ProviderFailureNormalizer.ShouldNormalize</c>
    /// sees a foreign exception rather than the <c>OperationCanceledException</c>
    /// it lets through, and stamps <c>upstream_error</c> on it. That identity
    /// is matched BEFORE the message patterns, so the timeout check below could
    /// never see it and every provider timeout was filed as ProviderError —
    /// undoing the distinction Phase 157 (K-737) exists to make.
    ///
    /// 🚨 The existing fake in <c>FailureManifests.ProviderTimeoutTests</c>
    /// cannot produce this: it throws <c>TaskCanceledException</c> DIRECTLY,
    /// which is never normalized, so it takes the unnormalized path instead.
    /// </remarks>
    [Theory]
    [InlineData("The model provider request failed. Provider: 'openai', fault: 'TaskCanceledException'.")]
    [InlineData("The model provider request failed. Provider: 'anthropic', fault: 'TimeoutException'.")]
    public void A_normalized_provider_timeout_is_a_timeout_not_a_generic_provider_error(string message)
    {
        var result = _classifier.Classify(new RunError { Type = "upstream_error", Message = message });

        result.Class.ShouldBe(RunErrorClass.Timeout);
    }

    /// <summary>
    /// A normalized provider failure that is NOT a timeout keeps its
    /// <see cref="RunErrorClass.ProviderError"/> class — the timeout rule above
    /// narrows one case, it does not replace the mapping.
    /// </summary>
    [Theory]
    [InlineData("The model provider request failed. Provider: 'openai', fault: 'ClientResultException' (HTTP 404).")]
    [InlineData("The model provider request failed. Provider: 'openrouter', fault: 'ClientResultException' (HTTP 402).")]
    public void A_normalized_provider_failure_that_is_not_a_timeout_stays_a_provider_error(string message)
    {
        var result = _classifier.Classify(new RunError { Type = "upstream_error", Message = message });

        result.Class.ShouldBe(RunErrorClass.ProviderError);
    }

    [Fact]
    public void The_same_error_always_produces_the_same_fingerprint()
    {
        var first = _classifier.Classify(new RunError { Type = "content_filtered", Message = "response was filtered" });
        var second = _classifier.Classify(new RunError { Type = "content_filtered", Message = "response was filtered" });

        first.Fingerprint.ShouldBe(second.Fingerprint);
    }
}
