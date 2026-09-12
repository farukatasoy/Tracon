using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Verifies that the eleven <c>Task&lt;IResult&gt;</c> endpoints that used to
/// report no response schema now report at least one (Phase 40, section 40.2).
/// </summary>
/// <remarks>
/// State measured before Phase 40: these eleven endpoints carried no entry
/// under <c>responses</c> at all (see <c>docs/arsiv/fazlar/40-OPENAPI-YAYINI.md</c>, "What
/// doesn't work today").
/// </remarks>
public sealed class OpenApiResponseSchemaTests
{
    /// <summary>The operationIds of the eleven endpoints that reported no response schema before Phase 40.</summary>
    public static TheoryData<string> HamTaskIResultOperationIds { get; } = new()
    {
        "TraconRunAgent",
        "TraconDownloadAttachment",
        "TraconRunWorkflow",
        "TraconResumeWorkflow",
        "TraconRespondWorkflowRequest",
        "TraconOpenAIResponses",
        "TraconOpenAIChatCompletions",
        "TraconOpenAICreateConversation",
        "TraconOpenAIGetConversation",
        "TraconOpenAIDeleteConversation",
        "TraconOpenAIListConversationItems",
    };

    [Fact]
    public async Task Every_endpoint_in_the_document_reports_at_least_one_response_schema()
    {
        var document = await FetchDocumentAsync();

        var missing = new List<string>();

        foreach (var (path, method, operation) in OpenApiTestHelpers.EnumerateOperations(document))
        {
            if (!operation.TryGetProperty("responses", out var responses) ||
                !responses.EnumerateObject().Any())
            {
                missing.Add($"{method.ToUpperInvariant()} {path}");
            }
        }

        missing.ShouldBeEmpty(customMessage: $"Endpoints with no response schema: {string.Join(", ", missing)}");
    }

    [Theory]
    [MemberData(nameof(HamTaskIResultOperationIds))]
    public async Task Endpoint_that_used_to_report_no_schema_now_reports_at_least_one(string operationId)
    {
        var document = await FetchDocumentAsync();

        var operation = OpenApiTestHelpers.FindByOperationId(document, operationId);

        var responses = operation.GetProperty("responses");

        responses.EnumerateObject().Count().ShouldBeGreaterThan(
            0, customMessage: $"'{operationId}' still reports no response schema.");
    }

    [Fact]
    public async Task Agent_run_endpoint_reports_both_SSE_and_ProblemDetails_errors()
    {
        var document = await FetchDocumentAsync();

        var operation = OpenApiTestHelpers.FindByOperationId(document, "TraconRunAgent");
        var responses = operation.GetProperty("responses");

        responses.GetProperty("200").GetProperty("content").TryGetProperty("text/event-stream", out _)
            .ShouldBeTrue("The success response must be reported as SSE.");

        foreach (var status in new[] { "400", "404", "429" })
        {
            responses.TryGetProperty(status, out var problemResponse).ShouldBeTrue(
                $"The '{status}' response is not reported.");

            problemResponse.GetProperty("content").TryGetProperty("application/problem+json", out _)
                .ShouldBeTrue($"The '{status}' response is not reported as ProblemDetails.");
        }
    }

    [Fact]
    public async Task Responses_endpoint_reports_both_JSON_and_SSE_success_responses()
    {
        var document = await FetchDocumentAsync();

        var operation = OpenApiTestHelpers.FindByOperationId(document, "TraconOpenAIResponses");
        var content = operation.GetProperty("responses").GetProperty("200").GetProperty("content");

        content.TryGetProperty("application/json", out _)
            .ShouldBeTrue("The non-streaming JSON response is not reported.");

        content.TryGetProperty("text/event-stream", out _)
            .ShouldBeTrue("The streaming SSE response is not reported.");
    }

    [Fact]
    public async Task Recorded_event_stream_endpoint_reports_SSE_and_404()
    {
        // Phase 145 (K-273 class): before this phase, this was the only SSE
        // endpoint in the family that reported NO content type at all -- the
        // "200" response had a bare description and no "content" key, so a
        // generated client treated the stream as plain JSON.
        var document = await FetchDocumentAsync();

        var operation = OpenApiTestHelpers.FindByOperationId(document, "TraconStreamRunEvents");
        var responses = operation.GetProperty("responses");

        responses.GetProperty("200").GetProperty("content").TryGetProperty("text/event-stream", out _)
            .ShouldBeTrue("The success response must be reported as SSE.");

        responses.TryGetProperty("404", out var notFoundResponse).ShouldBeTrue(
            "The '404' response (run does not exist) is not reported.");

        notFoundResponse.GetProperty("content").TryGetProperty("application/problem+json", out _)
            .ShouldBeTrue("The '404' response is not reported as ProblemDetails.");
    }

    private static async Task<System.Text.Json.JsonElement> FetchDocumentAsync()
    {
        await using var host = await TraconTestHost.StartAsync(withOpenApi: true);

        using var response = await host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        return await TraconTestHost.ReadJsonAsync(response);
    }
}
