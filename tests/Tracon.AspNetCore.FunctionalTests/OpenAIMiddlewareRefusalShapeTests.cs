using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The four middleware that turn an exception into a refusal answer an OpenAI-compatible route in the OpenAI error shape and
/// every other route as <c>ProblemDetails</c>.
/// </summary>
/// <remarks>
/// The middleware are driven directly because the fault each one maps (a provider that refuses a value, a store that does not
/// answer, a blank route value, a malformed body) cannot be made to happen on an OpenAI-compatible route of a running host
/// without a store built to fail. The routes are told apart by the metadata the real routes carry.
/// </remarks>
public sealed class OpenAIMiddlewareRefusalShapeTests
{
    public static TheoryData<string, int, string> Writers => new()
    {
        { "blank route value", StatusCodes.Status404NotFound, "not_found_error" },
        { "malformed body", StatusCodes.Status400BadRequest, "invalid_request_error" },
        { "refused value", StatusCodes.Status400BadRequest, "invalid_request_error" },
        { "store unavailable", StatusCodes.Status503ServiceUnavailable, "service_unavailable" },
    };

    [Theory]
    [MemberData(nameof(Writers))]
    public async Task On_an_openai_route_the_refusal_is_an_openai_error(string writer, int status, string type)
    {
        var (actualStatus, contentType, body) = await RunAsync(writer, openAiRoute: true);

        actualStatus.ShouldBe(status);
        contentType.ShouldBe("application/json");

        body.TryGetProperty("title", out _).ShouldBeFalse();
        body.GetProperty("error").GetProperty("type").GetString().ShouldBe(type);
        body.GetProperty("error").GetProperty("message").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public async Task On_any_other_route_the_refusal_stays_problem_details(string writer, int status, string type)
    {
        _ = type;

        var (actualStatus, contentType, body) = await RunAsync(writer, openAiRoute: false);

        actualStatus.ShouldBe(status);
        contentType.ShouldBe("application/problem+json");

        body.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
        body.GetProperty("status").GetInt32().ShouldBe(status);
        body.TryGetProperty("error", out _).ShouldBeFalse();
    }

    private static async Task<(int Status, string? ContentType, JsonElement Body)> RunAsync(string writer, bool openAiRoute)
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IStoreDataFaultClassifier>(new RefusingClassifier())
            .BuildServiceProvider();

        var metadata = new List<object> { new TagsAttribute("Tracon") };

        if (openAiRoute)
        {
            metadata.Add(OpenAIDenialShapeMarker.Instance);
        }

        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();
        context.SetEndpoint(new Endpoint(requestDelegate: null, new EndpointMetadataCollection(metadata), "test"));

        switch (writer)
        {
            case "blank route value":
                context.Request.RouteValues["conversationId"] = " ";
                await BlankRouteValueProblemMiddleware.InvokeAsync(context, static _ => throw BlankArgument(" "));
                break;

            case "malformed body":
                await JsonBindingProblemMiddleware.InvokeAsync(
                    context,
                    static _ => throw new BadHttpRequestException("bad body", new JsonException("unexpected token")));
                break;

            case "refused value":
                await StoreDataFaultProblemMiddleware.InvokeAsync(context, static _ => throw new InvalidOperationException("refused"));
                break;

            case "store unavailable":
                await StoreUnavailableProblemMiddleware.InvokeAsync(context, static _ => throw new TimeoutException("store did not answer"));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(writer), writer, "Unknown writer.");
        }

        context.Response.Body.Position = 0;
        using var document = JsonDocument.Parse(await new StreamReader(context.Response.Body).ReadToEndAsync());

        return (context.Response.StatusCode, context.Response.ContentType?.Split(';')[0], document.RootElement.Clone());
    }

    // The guard of a store throws a plain ArgumentException that names its parameter, as this does.
    private static ArgumentException BlankArgument(string conversationId) => new("blank", nameof(conversationId));

    private sealed class RefusingClassifier : IStoreDataFaultClassifier
    {
        public bool TryClassify(Exception exception, out StoreDataFault fault)
        {
            fault = StoreDataFault.TextTooLong;

            return true;
        }
    }
}
