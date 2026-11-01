using System.Net;
using System.Text;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A path segment that is only whitespace is a name nobody can have saved: no operation answers it with a 500.
/// </summary>
/// <remarks>
/// A store guards its arguments with <c>ArgumentException.ThrowIfNullOrWhiteSpace</c>, which is right for a caller in
/// code. An endpoint that hands the route value to the store unchecked turns that guard into a 500 for a request the
/// caller may well make by mistake (<c>/webhooks/%20/test</c>). The operations are taken from the published document,
/// so an endpoint added later is probed without anyone listing it.
/// </remarks>
public sealed class BlankRouteParameterTests
{
    [Theory]
    [InlineData("%20")]
    [InlineData("%0C")]
    public async Task No_operation_answers_500_when_a_string_path_parameter_is_blank(string blank)
    {
        await using var host = await OpenApiSnapshotTests.StartFullSurfaceHostAsync();

        using var documentResponse = await host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        documentResponse.EnsureSuccessStatusCode();

        var paths = (await TraconTestHost.ReadJsonAsync(documentResponse)).GetProperty("paths");
        var failures = new List<string>();
        var operations = 0;

        foreach (var path in paths.EnumerateObject())
        {
            var blanked = BlankStringParameters(path.Name, path.Value, blank);

            if (blanked is null)
            {
                continue;
            }

            foreach (var method in path.Value.EnumerateObject())
            {
                if (method.Name is not ("get" or "post" or "put" or "patch" or "delete"))
                {
                    continue;
                }

                operations++;

                using var request = new HttpRequestMessage(new HttpMethod(method.Name.ToUpperInvariant()), new Uri(blanked, UriKind.Relative));

                if (method.Name is "post" or "put" or "patch" && method.Value.TryGetProperty("requestBody", out _))
                {
                    request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                }

                try
                {
                    using var reply = await host.Client.SendAsync(request);

                    if ((int)reply.StatusCode >= 500 && reply.StatusCode != HttpStatusCode.NotImplemented)
                    {
                        failures.Add($"{method.Name.ToUpperInvariant()} {path.Name} -> {(int)reply.StatusCode}");
                    }
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NullReferenceException)
                {
                    // The test server hands an exception no middleware caught to the caller; a real host answers 500.
                    failures.Add($"{method.Name.ToUpperInvariant()} {path.Name} -> 500, {exception.GetType().Name}: {exception.Message}");
                }
            }
        }

        failures.ShouldBeEmpty();
        operations.ShouldBeGreaterThan(30, "the document lists too few operations with a string path parameter for the probe to mean anything");
    }

    [Theory]
    [InlineData("POST", "/tracon/api/webhooks/%20/test")]
    [InlineData("DELETE", "/tracon/api/sessions/%20")]
    [InlineData("GET", "/tracon/api/workflows/%20")]
    [InlineData("GET", "/tracon/api/tenants/%0C/egress")]
    public async Task A_blank_path_segment_is_a_404_problem_document(string method, string route)
    {
        await using var host = await TraconTestHost.StartAsync();

        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(route, UriKind.Relative));
        using var reply = await host.Client.SendAsync(request);

        reply.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        reply.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await TraconTestHost.ReadJsonAsync(reply)).GetProperty("title").GetString().ShouldBe(BlankRouteValueProblemMiddleware.ProblemTitle);
    }

    /// <summary>
    /// Replaces every string path parameter of the route with <paramref name="blank"/> and every other parameter with a
    /// value its type accepts, or returns <see langword="null"/> when the route has no string parameter.
    /// </summary>
    private static string? BlankStringParameters(string template, JsonElement pathItem, string blank)
    {
        var route = template;
        var any = false;

        foreach (var method in pathItem.EnumerateObject())
        {
            if (!method.Value.TryGetProperty("parameters", out var list))
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
                var token = "{" + parameter.GetProperty("name").GetString() + "}";

                if (string.Equals(format, "uuid", StringComparison.Ordinal))
                {
                    route = route.Replace(token, Guid.NewGuid().ToString(), StringComparison.Ordinal);
                }
                else if (string.Equals(type, "integer", StringComparison.Ordinal))
                {
                    route = route.Replace(token, "1", StringComparison.Ordinal);
                }
                else
                {
                    route = route.Replace(token, blank, StringComparison.Ordinal);
                    any = true;
                }
            }
        }

        return any ? route : null;
    }
}
