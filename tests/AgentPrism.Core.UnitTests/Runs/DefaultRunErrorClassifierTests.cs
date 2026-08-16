namespace AgentPrism.Core.UnitTests.Runs;

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
    // Measured against a real OpenAI 404 response (samples/AgentPrism.Api): the
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
    [InlineData("compilation_failed", "AgentPrism.AgentPrismCompilationException", RunErrorClass.CompilationFailed)]
    [InlineData("provider_unavailable", "AgentPrism.AgentPrismProviderUnavailableException", RunErrorClass.ProviderUnavailable)]
    public void Old_and_new_error_type_formats_map_to_the_same_class(string stableType, string legacyType, RunErrorClass expected)
    {
        var stable = _classifier.Classify(new RunError { Type = stableType, Message = "error" });
        var legacy = _classifier.Classify(new RunError { Type = legacyType, Message = "error" });

        stable.Class.ShouldBe(expected);
        legacy.Class.ShouldBe(expected);
    }

    [Fact]
    public void The_same_error_always_produces_the_same_fingerprint()
    {
        var first = _classifier.Classify(new RunError { Type = "content_filtered", Message = "response was filtered" });
        var second = _classifier.Classify(new RunError { Type = "content_filtered", Message = "response was filtered" });

        first.Fingerprint.ShouldBe(second.Fingerprint);
    }
}
