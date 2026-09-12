namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// Pins <see cref="FallbackRetryClassifier"/>'s decision table BEFORE the
/// Phase 113 (F-149) <see cref="IProviderRetryClassifier"/> seam was added to
/// <see cref="FallbackChatClient"/>. Tracon's own registered classifier
/// (<see cref="DefaultProviderRetryClassifier"/>) must reproduce the same
/// table bit-for-bit — a consumer who registers nothing must see IDENTICAL
/// retry behavior to before the seam existed.
/// </summary>
public sealed class FallbackRetryRegressionTests
{
    public static IEnumerable<object[]> DecisionTable()
    {
        yield return [new OperationCanceledException(), false, "a bare cancellation never retries"];
        yield return [
            new AggregateException("wrapped", new OperationCanceledException()),
            false,
            "a cancellation buried in an AggregateException never retries",
        ];
        // 🚨 Phase 157 (K-737). The two rows above and the two below share the
        // same exception FAMILY and split on whether a timeout is in the graph.
        // HttpClient reports its own request timeout as a
        // TaskCanceledException, so the old "any OperationCanceledException is
        // a cancellation" rule made a timed-out provider the one failure a
        // fallback chain never routed around.
        yield return [
            new TaskCanceledException(
                "The request to https://api.example/v1 timed out after 30s.",
                new TimeoutException("The request timed out.")),
            true,
            "an HttpClient timeout retries the next link, even though its type is a cancellation",
        ];
        yield return [
            new AggregateException("wrapped", new TimeoutException("the operation timed out")),
            true,
            "a timeout buried in an AggregateException retries",
        ];
        yield return [new InvalidOperationException("HTTP 401 (invalid_api_key)"), false, "401 is never retried"];
        yield return [new InvalidOperationException("HTTP 403 (forbidden)"), false, "403 is never retried"];
        yield return [
            new AggregateException(
                "Retry failed after 4 tries. (HTTP 401 (invalid_api_key))",
                new FakeClientResultException("HTTP 401 (invalid_api_key)")),
            false,
            "auth wins even when wrapped alongside a status-less transport type",
        ];
        yield return [new InvalidOperationException("HTTP 429 (rate_limit_exceeded)"), true, "429 retries"];
        yield return [new InvalidOperationException("rate limit exceeded, please retry later"), true, "rate-limit text retries"];
        yield return [new InvalidOperationException("toomanyrequests"), true, "\"too many requests\" text retries"];
        yield return [new InvalidOperationException("HTTP 500 (internal_server_error)"), true, "5xx retries"];
        yield return [new InvalidOperationException("HTTP 503 (service_unavailable)"), true, "503 retries"];
        yield return [
            new TraconProviderUnavailableException("circuit open"),
            true,
            "an already-open circuit retries the next link",
        ];
        yield return [
            new AggregateException(
                "Retry failed after 4 tries. (Connection refused (127.0.0.1:1))",
                new FakeClientResultException("Connection refused (127.0.0.1:1)")),
            true,
            "a status-less SDK wrapper exception is a connection failure and retries",
        ];
        yield return [new HttpRequestException("connection reset"), true, "HttpRequestException by type name retries"];
        yield return [new InvalidOperationException("something nobody anticipated"), false, "an unrecognized failure never retries by default"];
    }

    [Theory]
    [MemberData(nameof(DecisionTable))]
    public void FallbackRetryClassifier_matches_the_decision_table(Exception exception, bool expectedRetryable, string because)
    {
        FallbackRetryClassifier.IsRetryable(exception).ShouldBe(expectedRetryable, because);
    }

    [Theory]
    [MemberData(nameof(DecisionTable))]
    public void DefaultProviderRetryClassifier_reproduces_the_same_table(Exception exception, bool expectedRetryable, string because)
    {
        var decision = new DefaultProviderRetryClassifier().Classify(exception);

        decision.ShouldBe(
            expectedRetryable ? ProviderRetryDecision.Retry : ProviderRetryDecision.DoNotRetry,
            because);
    }

    /// <summary>Mimics <c>System.ClientModel.ClientResultException</c> BY NAME, for classifier tests that cannot reference the real SDK type.</summary>
    private sealed class FakeClientResultException(string message) : Exception(message);
}
