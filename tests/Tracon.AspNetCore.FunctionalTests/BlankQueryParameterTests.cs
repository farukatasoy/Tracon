using System.Net;
using System.Text;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A query parameter that is only whitespace is a value nobody can have stored: no operation answers it with a 500.
/// </summary>
/// <remarks>
/// The sibling of the blank path segment (<see cref="BlankRouteParameterTests"/>): a service behind the endpoint guards its
/// argument with <c>ArgumentException.ThrowIfNullOrWhiteSpace</c>, and an endpoint that checks only for an empty string lets
/// <c>?state=%0C</c> through to it. The operations and their string parameters come from the published document, so a
/// parameter added later is tried without anyone listing it.
/// </remarks>
public sealed class BlankQueryParameterTests
{
    [Theory]
    [InlineData("%20")]
    [InlineData("%0C")]
    public async Task No_operation_answers_5xx_when_a_string_query_parameter_is_blank(string blank)
    {
        // The MCP layer is registered: without it the OAuth callback answers a fixed page and never reaches the coordinator.
        await using var host = await OpenApiSnapshotTests.StartFullSurfaceHostAsync(configureMoreTracon: static builder => builder.UseMcp());

        using var documentResponse = await host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        documentResponse.EnsureSuccessStatusCode();

        var paths = (await TraconTestHost.ReadJsonAsync(documentResponse)).GetProperty("paths");
        var failures = new List<string>();
        var probed = 0;

        foreach (var path in paths.EnumerateObject())
        {
            foreach (var method in path.Value.EnumerateObject())
            {
                if (method.Name is not ("get" or "post" or "put" or "delete") || !method.Value.TryGetProperty("parameters", out var parameters))
                {
                    continue;
                }

                var route = FillPathParameters(path.Name, parameters);

                foreach (var parameter in parameters.EnumerateArray())
                {
                    if (!string.Equals(parameter.GetProperty("in").GetString(), "query", StringComparison.Ordinal)
                        || !IsPlainString(parameter.GetProperty("schema")))
                    {
                        continue;
                    }

                    probed++;
                    var name = parameter.GetProperty("name").GetString();

                    using var request = new HttpRequestMessage(new HttpMethod(method.Name.ToUpperInvariant()), new Uri($"{route}?{name}={blank}", UriKind.Relative));

                    if (method.Name is "post" or "put" && method.Value.TryGetProperty("requestBody", out _))
                    {
                        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                    }

                    try
                    {
                        using var reply = await host.Client.SendAsync(request);

                        if ((int)reply.StatusCode >= 500 && reply.StatusCode != HttpStatusCode.NotImplemented)
                        {
                            failures.Add($"{method.Name.ToUpperInvariant()} {path.Name}?{name}= -> {(int)reply.StatusCode}");
                        }
                    }
                    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NullReferenceException)
                    {
                        // The test server hands an exception no middleware caught to the caller; a real host answers 500.
                        failures.Add($"{method.Name.ToUpperInvariant()} {path.Name}?{name}= -> 500, {exception.GetType().Name}: {exception.Message}");
                    }
                }
            }
        }

        failures.ShouldBeEmpty();
        probed.ShouldBeGreaterThan(30, "the document lists too few string query parameters for the probe to mean anything");
    }

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

    /// <summary>A string with no format and no enum: the kind a blank value is a mistake for, not a date, a GUID or a member.</summary>
    private static bool IsPlainString(JsonElement schema)
    {
        if (schema.TryGetProperty("enum", out _) || schema.TryGetProperty("format", out _))
        {
            return false;
        }

        if (!schema.TryGetProperty("type", out var type))
        {
            return false;
        }

        return type.ValueKind == JsonValueKind.String
            ? string.Equals(type.GetString(), "string", StringComparison.Ordinal)
            : type.EnumerateArray().Any(static kind => string.Equals(kind.GetString(), "string", StringComparison.Ordinal))
              && !type.EnumerateArray().Any(static kind => string.Equals(kind.GetString(), "integer", StringComparison.Ordinal));
    }
}
