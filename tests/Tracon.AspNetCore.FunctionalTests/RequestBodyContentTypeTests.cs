using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A request whose body is missing, or is not JSON, is the caller's mistake: no operation answers it with a 500.
/// </summary>
/// <remarks>
/// <para>
/// An endpoint that reads its body by hand and does not look at the content type first lets the framework's
/// <c>InvalidOperationException</c> ("not a known JSON content type") out, and the caller gets an empty 500 for
/// forgetting a header. Thirty-three operations did, found by sending each of them nothing at all. The operations
/// are taken from the published document, so an endpoint added later is probed without anyone listing it.
/// </para>
/// <para>
/// The wrong content type is 415, the code the framework's own body binding and the definition import already answer.
/// </para>
/// </remarks>
public sealed class RequestBodyContentTypeTests
{
    private static readonly string[] BodyMethods = ["post", "put", "patch"];

    [Fact]
    public async Task No_operation_that_takes_a_body_answers_500_when_the_request_has_none_or_is_not_JSON()
    {
        await using var host = await OpenApiSnapshotTests.StartFullSurfaceHostAsync();

        using var documentResponse = await host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        documentResponse.EnsureSuccessStatusCode();

        var paths = (await TraconTestHost.ReadJsonAsync(documentResponse)).GetProperty("paths");
        var failures = new List<string>();
        var operations = 0;

        foreach (var path in paths.EnumerateObject())
        {
            foreach (var method in BodyMethods)
            {
                if (!path.Value.TryGetProperty(method, out var operation) || !operation.TryGetProperty("requestBody", out _))
                {
                    continue;
                }

                var uri = new Uri(FillRoute(path.Name, path.Value, operation), UriKind.Relative);
                operations++;

                foreach (var variant in new[] { "no body", "text/plain" })
                {
                    using var request = new HttpRequestMessage(new HttpMethod(method.ToUpperInvariant()), uri);

                    if (string.Equals(variant, "text/plain", StringComparison.Ordinal))
                    {
                        request.Content = new StringContent("not json", Encoding.UTF8, "text/plain");
                    }

                    try
                    {
                        using var reply = await host.Client.SendAsync(request);

                        // 501 is the answer of an endpoint whose capability is not registered in this host (workflows,
                        // knowledge, MCP prompts); it says nothing about the body.
                        if ((int)reply.StatusCode >= 500 && reply.StatusCode != HttpStatusCode.NotImplemented)
                        {
                            failures.Add($"{method.ToUpperInvariant()} {path.Name} ({variant}) -> {(int)reply.StatusCode}");
                        }
                    }
                    catch (InvalidOperationException exception)
                    {
                        // The test server hands an exception no middleware caught to the caller; a real host answers 500.
                        failures.Add($"{method.ToUpperInvariant()} {path.Name} ({variant}) -> 500, {exception.Message}");
                    }
                }
            }
        }

        failures.ShouldBeEmpty();
        operations.ShouldBeGreaterThan(40, "the document lists too few operations with a body for the probe to mean anything");
    }

    [Fact]
    public async Task The_attachment_upload_refuses_a_request_with_no_body_with_415_and_a_problem_document()
    {
        // The request the generated request delegate used to let through to a 500: it reads a form only when the request
        // can carry a body, and a request with no body has no content type for the framework's own match to refuse.
        await using var host = await TraconTestHost.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/tracon/api/attachments", UriKind.Relative));
        using var reply = await host.Client.SendAsync(request);

        reply.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        reply.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("application/problem+json");

        var problem = await TraconTestHost.ReadJsonAsync(reply);
        (problem.GetProperty("detail").GetString() ?? string.Empty).ShouldContain("multipart/form-data");
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/json")]
    public async Task The_attachment_upload_refuses_a_body_that_is_not_a_form_with_415(string contentType)
    {
        // Answered by the routing layer's own content type match, before any Tracon middleware: a bare 415, as before.
        await using var host = await TraconTestHost.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/tracon/api/attachments", UriKind.Relative))
        {
            Content = new StringContent("not a form", Encoding.UTF8, contentType),
        };

        using var reply = await host.Client.SendAsync(request);

        reply.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
    }

    [Theory]
    [InlineData("POST", "/tracon/api/agents")]
    [InlineData("PUT", "/tracon/api/workflows/probe")]
    [InlineData("POST", "/tracon/api/agents/validate")]
    [InlineData("PUT", "/tracon/api/quotas")]
    public async Task A_body_that_is_not_JSON_is_refused_with_415_and_a_problem_document(string method, string route)
    {
        await using var host = await TraconTestHost.StartAsync();

        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(route, UriKind.Relative))
        {
            Content = new StringContent("name: probe", Encoding.UTF8, "text/plain"),
        };

        using var reply = await host.Client.SendAsync(request);

        reply.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        reply.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Theory]
    [InlineData("/tracon/v1/chat/completions")]
    [InlineData("/tracon/v1/responses")]
    public async Task The_OpenAI_compatible_routes_refuse_a_body_that_is_not_JSON_with_415_in_their_own_error_shape(string route)
    {
        await using var host = await TraconTestHost.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(route, UriKind.Relative));

        using var reply = await host.Client.SendAsync(request);

        reply.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);

        (await TraconTestHost.ReadJsonAsync(reply)).GetProperty("error").GetProperty("message").GetString()
            .ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Puts a value in every route parameter of the operation: a GUID where the schema says <c>uuid</c>, a number where
    /// it says integer, a word otherwise. A route that cannot match answers 404, which would make the probe pass
    /// without ever reaching the body.
    /// </summary>
    internal static string FillRoute(string template, JsonElement pathItem, JsonElement operation)
    {
        var route = template;

        foreach (var parameters in new[] { pathItem, operation })
        {
            if (!parameters.TryGetProperty("parameters", out var list))
            {
                continue;
            }

            foreach (var parameter in list.EnumerateArray())
            {
                if (!string.Equals(parameter.GetProperty("in").GetString(), "path", StringComparison.Ordinal))
                {
                    continue;
                }

                var schema = parameter.GetProperty("schema");
                var format = schema.TryGetProperty("format", out var f) ? f.GetString() : null;
                var type = schema.TryGetProperty("type", out var t) ? t.GetString() : null;

                var value = string.Equals(format, "uuid", StringComparison.Ordinal)
                    ? Guid.NewGuid().ToString()
                    : string.Equals(type, "integer", StringComparison.Ordinal)
                        ? 1.ToString(CultureInfo.InvariantCulture)
                        : "probe";

                route = route.Replace("{" + parameter.GetProperty("name").GetString() + "}", value, StringComparison.Ordinal);
            }
        }

        return route;
    }
}
