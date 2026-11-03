using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A number that names no member of an enum query parameter is a bad request, never a server error.
/// </summary>
/// <remarks>
/// <para>
/// The binder parses an enum parameter with <c>Enum.TryParse</c>, which accepts any integer: <c>?bucket=99</c> binds to a
/// value that is in no member. A store that turns the bucket into a date expression with a <c>switch</c> that throws on
/// the unknown value then answers 500 for a request the caller may well have made by mistake. Text that names no member
/// is already a 400 (the binder rejects it); the number is the same mistake in another spelling.
/// </para>
/// <para>
/// The probe is taken from the published document, so an enum parameter added later is tried without anyone listing it.
/// The host holds one score and one run first: a store that evaluates the value per row only reaches it with a row to
/// evaluate, which an empty store never does.
/// </para>
/// <para>
/// Not a 5xx is the floor; the answer is the same 400 the binder gives for text that names no member, for every enum
/// parameter of every operation, so a handler never has to remember to ask.
/// </para>
/// </remarks>
public sealed class UndefinedEnumParameterTests
{
    [Theory]
    [InlineData("-1")]
    [InlineData("99")]
    public async Task No_operation_answers_5xx_when_an_enum_query_parameter_is_a_number_that_names_no_member(string number)
    {
        await using var host = await OpenApiSnapshotTests.StartFullSurfaceHostAsync();
        await SeedRowsAsync(host);

        using var documentResponse = await host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        documentResponse.EnsureSuccessStatusCode();

        var document = await TraconTestHost.ReadJsonAsync(documentResponse);
        var components = document.GetProperty("components").GetProperty("schemas");
        var failures = new List<string>();
        var probed = 0;

        foreach (var path in document.GetProperty("paths").EnumerateObject())
        {
            if (!path.Value.TryGetProperty("get", out var operation) || !operation.TryGetProperty("parameters", out var parameters))
            {
                continue;
            }

            var route = FillPathParameters(path.Name, parameters);

            foreach (var parameter in parameters.EnumerateArray())
            {
                if (!string.Equals(parameter.GetProperty("in").GetString(), "query", StringComparison.Ordinal)
                    || !IsEnum(parameter.GetProperty("schema"), components))
                {
                    continue;
                }

                probed++;
                var name = parameter.GetProperty("name").GetString();

                try
                {
                    using var reply = await host.Client.GetAsync(new Uri($"{route}?{name}={number}", UriKind.Relative));

                    if ((int)reply.StatusCode >= 500)
                    {
                        failures.Add($"GET {path.Name}?{name}={number} -> {(int)reply.StatusCode}");
                    }
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NullReferenceException)
                {
                    // The test server hands an exception no middleware caught to the caller; a real host answers 500.
                    failures.Add($"GET {path.Name}?{name}={number} -> 500, {exception.GetType().Name}: {exception.Message}");
                }
            }
        }

        failures.ShouldBeEmpty();
        probed.ShouldBeGreaterThan(5, "the document lists too few enum query parameters for the probe to mean anything");
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("99")]
    public async Task Every_enum_query_parameter_answers_400_for_a_number_that_names_no_member(string number)
    {
        await using var host = await OpenApiSnapshotTests.StartFullSurfaceHostAsync();
        await SeedRowsAsync(host);

        using var documentResponse = await host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        documentResponse.EnsureSuccessStatusCode();

        var document = await TraconTestHost.ReadJsonAsync(documentResponse);
        var components = document.GetProperty("components").GetProperty("schemas");
        var failures = new List<string>();
        var probed = 0;

        foreach (var path in document.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (operation.Name is not ("get" or "post" or "put" or "patch" or "delete")
                    || !operation.Value.TryGetProperty("parameters", out var parameters))
                {
                    continue;
                }

                var route = FillPathParameters(path.Name, parameters);

                foreach (var parameter in parameters.EnumerateArray())
                {
                    if (!string.Equals(parameter.GetProperty("in").GetString(), "query", StringComparison.Ordinal)
                        || !IsEnum(parameter.GetProperty("schema"), components))
                    {
                        continue;
                    }

                    probed++;
                    var name = parameter.GetProperty("name").GetString();
                    var label = $"{operation.Name.ToUpperInvariant()} {path.Name}?{name}={number}";

                    using var request = new HttpRequestMessage(
                        new HttpMethod(operation.Name.ToUpperInvariant()),
                        new Uri($"{route}?{name}={number}", UriKind.Relative));

                    if (operation.Value.TryGetProperty("requestBody", out _))
                    {
                        request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
                    }

                    try
                    {
                        using var reply = await host.Client.SendAsync(request);
                        var body = await reply.Content.ReadAsStringAsync();

                        if (reply.StatusCode != HttpStatusCode.BadRequest
                            || !string.Equals(reply.Content.Headers.ContentType?.MediaType, "application/problem+json", StringComparison.Ordinal)
                            || !body.Contains($"'{name}'", StringComparison.Ordinal))
                        {
                            failures.Add($"{label} -> {(int)reply.StatusCode}");
                        }
                    }
                    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NullReferenceException)
                    {
                        failures.Add($"{label} -> 500, {exception.GetType().Name}: {exception.Message}");
                    }
                }
            }
        }

        failures.ShouldBeEmpty();
        probed.ShouldBeGreaterThan(5, "the document lists too few enum query parameters for the probe to mean anything");
    }

    [Theory]
    [InlineData("/tracon/api/evaluation/scores/summary?bucket=99", "bucket", "Hour, Day, Week")]
    [InlineData("/tracon/api/evaluation/scores/summary?bucket=0", "bucket", "Hour, Day, Week")]
    [InlineData("/tracon/api/stats/timeseries?bucket=-1", "bucket", "Hour, Day")]
    [InlineData("/tracon/api/stats/timeseries?bucket=99", "bucket", "Hour, Day")]
    public async Task A_bucket_that_names_no_member_is_a_400_that_lists_the_members(string route, string parameter, string members)
    {
        await using var host = await TraconTestHost.StartAsync();
        await SeedRowsAsync(host);

        using var response = await host.Client.GetAsync(new Uri(route, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = await TraconTestHost.ReadJsonAsync(response);
        problem.GetProperty("title").GetString().ShouldBe("Invalid request parameter");
        var detail = problem.GetProperty("detail").GetString() ?? string.Empty;
        detail.ShouldContain($"'{parameter}'");
        detail.ShouldContain(members);
    }

    [Theory]
    [InlineData("/tracon/api/evaluation/scores/summary?bucket=Day")]
    [InlineData("/tracon/api/evaluation/scores/summary?bucket=2")]
    [InlineData("/tracon/api/stats/timeseries?bucket=Day")]
    [InlineData("/tracon/api/stats/timeseries?bucket=0")]
    public async Task A_bucket_that_names_a_member_by_name_or_by_number_is_still_accepted(string route)
    {
        await using var host = await TraconTestHost.StartAsync();
        await SeedRowsAsync(host);

        using var response = await host.Client.GetAsync(new Uri(route, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task SeedRowsAsync(TraconTestHost host)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();
        var scores = host.Services.GetRequiredService<IRunScoreStore>();
        var runId = TraconId.NewId();
        var now = DateTimeOffset.UtcNow;

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "probe-agent",
            StartedAt = now,
            TenantId = "default",
        });

        await scores.UpsertAsync(new RunScore
        {
            TenantId = "default",
            RunId = runId,
            Name = "helpfulness",
            Kind = RunScoreKind.Binary,
            Value = 1,
            Source = "human",
            Author = "probe",
            CreatedAt = now,
        });
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

    /// <summary>Whether the schema is an enum, directly or through a reference or a nullable union.</summary>
    private static bool IsEnum(JsonElement schema, JsonElement components)
    {
        if (schema.TryGetProperty("enum", out _))
        {
            return true;
        }

        if (schema.TryGetProperty("$ref", out var reference))
        {
            var name = reference.GetString()?.Split('/')[^1];

            return name is not null && components.TryGetProperty(name, out var target) && IsEnum(target, components);
        }

        foreach (var union in new[] { "oneOf", "anyOf" })
        {
            if (schema.TryGetProperty(union, out var branches) && branches.EnumerateArray().Any(branch => IsEnum(branch, components)))
            {
                return true;
            }
        }

        return false;
    }
}
