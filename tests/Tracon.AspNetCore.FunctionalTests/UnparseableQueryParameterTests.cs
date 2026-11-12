using System.Net;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A query parameter whose text cannot be read as the number, date, flag or GUID it stands for is answered with a problem
/// document that names the parameter and the form it takes, in every environment.
/// </summary>
/// <remarks>
/// <para>
/// The enum query parameters have had this since the enum filter: the request delegate generator does not throw for a
/// nullable parameter in any environment, it marks the request <c>400</c> and goes on, and the answer was a bare <c>400</c>
/// with no body (in Development too, where an exception handler hides nothing because nothing is thrown). A date range that
/// reads <c>?startedAfter=yesterday</c> told the caller nothing about which parameter was wrong.
/// </para>
/// <para>
/// The environment is a parameter on purpose: Development turns on a flag in the framework that changes what a failing
/// binding does for the parameters that are not nullable.
/// </para>
/// </remarks>
public sealed class UnparseableQueryParameterTests
{
    [Theory]
    [InlineData(null, "/tracon/api/runs?startedAfter=yesterday", "startedAfter")]
    [InlineData("Development", "/tracon/api/runs?startedAfter=yesterday", "startedAfter")]
    [InlineData(null, "/tracon/api/runs?take=many", "take")]
    [InlineData("Development", "/tracon/api/runs?take=many", "take")]
    [InlineData(null, "/tracon/api/runs?includeChildren=maybe", "includeChildren")]
    [InlineData(null, "/tracon/api/runs?parentRunId=nope", "parentRunId")]
    [InlineData(null, "/tracon/api/stats/errors?hours=x", "hours")]
    [InlineData("Development", "/tracon/api/stats/errors?hours=x", "hours")]
    // A parameter that is not nullable and has no default: the framework throws for it in Development.
    [InlineData(null, "/tracon/api/evals/runs/2f0f6c7e-0000-4000-8000-000000000000/diff?baseline=nope", "baseline")]
    [InlineData("Development", "/tracon/api/evals/runs/2f0f6c7e-0000-4000-8000-000000000000/diff?baseline=nope", "baseline")]
    public async Task An_unparseable_query_value_names_the_parameter(string? environment, string path, string parameter)
    {
        await using var host = await TraconTestHost.StartAsync(environment: environment);

        using var response = await host.Client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, text);
        response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("application/problem+json", text);

        var json = await TraconTestHost.ReadJsonAsync(response);

        json.GetProperty("title").GetString().ShouldBe("Invalid request parameter");

        // Development throws for a parameter that is not nullable and the middleware that catches it writes the framework's
        // own sentence ("Failed to bind parameter "Guid baseline" ..."), which names the parameter without the quotes.
        json.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain(parameter, Case.Sensitive);
    }

    [Fact]
    public async Task The_detail_says_the_form_the_value_takes()
    {
        await using var host = await TraconTestHost.StartAsync();

        foreach (var (path, expected) in new[]
        {
            ("/tracon/api/runs?take=many", "a whole number"),
            ("/tracon/api/runs?includeChildren=maybe", "true or false"),
            ("/tracon/api/runs?parentRunId=nope", "a GUID"),
            ("/tracon/api/runs?startedAfter=yesterday", "ISO 8601"),
            ("/tracon/api/stats/errors?hours=x", "a number"),
        })
        {
            using var response = await host.Client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
            var detail = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString();

            detail.ShouldNotBeNull().ShouldContain(expected, Case.Sensitive, path);
        }
    }

    [Fact]
    public async Task A_value_that_reads_is_not_refused()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/api/runs?take=5&includeChildren=true&startedAfter=2026-11-12T08:00:00Z", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// The probe is taken from the published document: every query parameter that is a number, a flag, a GUID or a date is
    /// sent text that is none of them, and the answer names it.
    /// </summary>
    [Fact]
    public async Task Every_scalar_query_parameter_of_the_document_names_itself_when_it_cannot_be_read()
    {
        await using var host = await OpenApiSnapshotTests.StartFullSurfaceHostAsync();

        using var documentResponse = await host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);
        documentResponse.EnsureSuccessStatusCode();

        var document = await TraconTestHost.ReadJsonAsync(documentResponse);
        var failures = new List<string>();
        var probed = 0;

        foreach (var (path, method, operation) in OpenApiTestHelpers.EnumerateOperations(document))
        {
            if (!string.Equals(method, "get", StringComparison.Ordinal) || !operation.TryGetProperty("parameters", out var parameters))
            {
                continue;
            }

            var route = FillPathParameters(path, parameters);

            foreach (var parameter in parameters.EnumerateArray())
            {
                if (!string.Equals(parameter.GetProperty("in").GetString(), "query", StringComparison.Ordinal)
                    || !IsScalar(parameter.GetProperty("schema")))
                {
                    continue;
                }

                probed++;
                var name = parameter.GetProperty("name").GetString();
                var label = $"GET {path}?{name}=not-a-value";

                try
                {
                    using var reply = await host.Client.GetAsync(new Uri($"{route}?{name}=not-a-value", UriKind.Relative), TestContext.Current.CancellationToken);
                    // The OpenAI-compatible routes write their JSON with the quote escaped.
                    var body = (await reply.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Replace("\\u0027", "'", StringComparison.Ordinal);

                    if (reply.StatusCode != HttpStatusCode.BadRequest || !body.Contains($"'{name}'", StringComparison.Ordinal))
                    {
                        failures.Add($"{label} -> {(int)reply.StatusCode} {(body.Length == 0 ? "(no body)" : body[..Math.Min(body.Length, 80)])}");
                    }
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NullReferenceException)
                {
                    failures.Add($"{label} -> 500, {exception.GetType().Name}: {exception.Message}");
                }
            }
        }

        failures.ShouldBeEmpty();
        probed.ShouldBeGreaterThan(20, "the document lists too few scalar query parameters for the probe to mean anything");
    }

    /// <summary>Whether the schema is a number, a flag, a GUID or a date, directly or inside a nullable union.</summary>
    private static bool IsScalar(JsonElement schema)
    {
        if (schema.TryGetProperty("type", out var type))
        {
            // OpenAPI 3.1 writes a nullable type as an array: ["integer", "null"].
            var kinds = type.ValueKind == JsonValueKind.Array
                ? type.EnumerateArray().Select(static kind => kind.GetString()).ToList()
                : [type.GetString()];

            if (kinds.Any(static kind => kind is "integer" or "number" or "boolean"))
            {
                return true;
            }

            if (kinds.Contains("string", StringComparer.Ordinal)
                && schema.TryGetProperty("format", out var format)
                && format.GetString() is "uuid" or "date-time" or "date" or "time" or "duration")
            {
                return true;
            }
        }

        foreach (var keyword in new[] { "anyOf", "oneOf" })
        {
            if (schema.TryGetProperty(keyword, out var branches) && branches.EnumerateArray().Any(IsScalar))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Replaces every path parameter with a value its type accepts.</summary>
    private static string FillPathParameters(string template, JsonElement parameters)
    {
        var route = template;

        foreach (var parameter in parameters.EnumerateArray())
        {
            if (!string.Equals(parameter.GetProperty("in").GetString(), "path", StringComparison.Ordinal))
            {
                continue;
            }

            var schema = parameter.GetProperty("schema");
            var format = schema.TryGetProperty("format", out var f) ? f.GetString() : null;
            var type = schema.TryGetProperty("type", out var t) ? t.GetString() : null;
            var value = string.Equals(format, "uuid", StringComparison.Ordinal) ? Guid.NewGuid().ToString()
                : string.Equals(type, "integer", StringComparison.Ordinal) ? "1"
                : "probe";
            route = route.Replace("{" + parameter.GetProperty("name").GetString() + "}", value, StringComparison.Ordinal);
        }

        return route;
    }
}
