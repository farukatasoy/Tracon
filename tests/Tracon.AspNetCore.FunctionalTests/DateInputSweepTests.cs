using System.Net;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Tests.Common;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// No date or time a caller sends makes the server fail, whatever holds the data, and the sweep that proves it covers
/// every such input of the published document.
/// </summary>
/// <remarks>
/// The sweep is written once (<see cref="DateInputSweep"/>) and runs here on the in-memory store and on SQLite; the
/// two-node project runs it on PostgreSQL and SQL Server, where the date types have edges of their own.
/// </remarks>
public sealed class DateInputSweepTests
{
    private const string ApiPrefix = "/tracon/api/";

    [Fact]
    public async Task No_date_input_makes_the_in_memory_store_fail()
    {
        await using var host = await TraconTestHost.StartAsync();
        await DateInputSweep.SeedEdgeRowsAsync(host.Services);

        (await DateInputSweep.FindServerErrorsAsync(host.Client)).ShouldBeEmpty();
    }

    [Fact]
    public async Task No_date_input_makes_the_SQLite_store_fail()
    {
        using var database = new TempSqliteDatabase("date-input-sweep");
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: tracon => tracon.UseSqlite(options => options.ConnectionString = database.ConnectionString));
        await DateInputSweep.SeedEdgeRowsAsync(host.Services);

        (await DateInputSweep.FindServerErrorsAsync(host.Client)).ShouldBeEmpty();
    }

    /// <summary>
    /// A range without a start begins 24 hours before its end, and an end that has no such hour before it has no start
    /// that can be written down: the answer starts at the first instant, and an empty range is a 400.
    /// </summary>
    [Theory]
    [InlineData("0001-01-01T10:00:00Z", HttpStatusCode.OK, 10)]
    [InlineData("0001-01-02T00:00:00Z", HttpStatusCode.OK, 24)]
    [InlineData("0001-01-02T10:00:00Z", HttpStatusCode.OK, 24)]
    [InlineData("0001-01-01T00:00:00Z", HttpStatusCode.BadRequest, 0)]
    [InlineData("0001-01-01T14:00:00+14:00", HttpStatusCode.BadRequest, 0)]
    public async Task A_range_that_ends_in_the_first_day_of_the_calendar_has_a_start_or_is_refused(
        string to,
        HttpStatusCode expected,
        int buckets)
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri(
            $"{ApiPrefix}stats/timeseries?to={Uri.EscapeDataString(to)}",
            UriKind.Relative));

        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync());

        if (expected == HttpStatusCode.OK)
        {
            (await TraconTestHost.ReadJsonAsync(response)).GetArrayLength().ShouldBe(buckets);
        }
    }

    /// <summary>
    /// The sweep drives exactly the date inputs the document names: a new input that nobody added to the sweep, or an
    /// input the document no longer has, is a failure here and not a gap that stays unseen.
    /// </summary>
    [Fact]
    public async Task The_sweep_drives_every_date_input_of_the_published_document_and_nothing_else()
    {
        await using var host = await TraconTestHost.StartAsync(withOpenApi: true);

        using var response = await host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        var document = await TraconTestHost.ReadJsonAsync(response);

        var inDocument = DateInputsOf(document).ToHashSet(StringComparer.Ordinal);
        var inSweep = DateInputSweep.Inputs
            .SelectMany(static input => input.Second is null
                ? [$"{input.Method} {input.Path} {input.First}"]
                : new[] { $"{input.Method} {input.Path} {input.First}", $"{input.Method} {input.Path} {input.Second}" })
            .ToHashSet(StringComparer.Ordinal);

        inDocument.Except(inSweep, StringComparer.Ordinal).Order(StringComparer.Ordinal).ShouldBeEmpty("The document has date inputs the sweep does not drive.");
        inSweep.Except(inDocument, StringComparer.Ordinal).Order(StringComparer.Ordinal).ShouldBeEmpty("The sweep drives date inputs the document does not have.");
    }

    private static IEnumerable<string> DateInputsOf(JsonElement document)
    {
        var schemas = document.GetProperty("components").GetProperty("schemas");

        foreach (var (path, method, operation) in OpenApiTestHelpers.EnumerateOperations(document))
        {
            var relative = path.StartsWith(ApiPrefix[..^1], StringComparison.Ordinal) ? path[(ApiPrefix.Length - 1)..] : path;
            var verb = method.ToUpperInvariant();

            if (operation.TryGetProperty("parameters", out var parameters))
            {
                foreach (var parameter in parameters.EnumerateArray())
                {
                    if (parameter.TryGetProperty("schema", out var schema) && HoldsDate(schema, schemas, []))
                    {
                        yield return $"{verb} {relative} {parameter.GetProperty("name").GetString()}";
                    }
                }
            }

            if (operation.TryGetProperty("requestBody", out var body) && body.TryGetProperty("content", out var content))
            {
                foreach (var media in content.EnumerateObject())
                {
                    if (!media.Value.TryGetProperty("schema", out var schema))
                    {
                        continue;
                    }

                    foreach (var member in DateMembersOf(schema, schemas, [], string.Empty))
                    {
                        yield return $"{verb} {relative} {member}";
                    }
                }
            }
        }
    }

    private static bool IsDate(JsonElement schema)
        => schema.TryGetProperty("format", out var format) && format.GetString() is "date-time" or "date";

    private static bool HoldsDate(JsonElement schema, JsonElement schemas, IReadOnlyList<string> stack)
    {
        if (IsDate(schema))
        {
            return true;
        }

        foreach (var keyword in new[] { "anyOf", "oneOf", "allOf" })
        {
            if (schema.TryGetProperty(keyword, out var alternatives) &&
                alternatives.EnumerateArray().Any(alternative => HoldsDate(alternative, schemas, stack)))
            {
                return true;
            }
        }

        return Resolve(schema, schemas, stack, out var resolved, out var deeper) && HoldsDate(resolved, schemas, deeper);
    }

    private static IEnumerable<string> DateMembersOf(JsonElement schema, JsonElement schemas, IReadOnlyList<string> stack, string path)
    {
        if (Resolve(schema, schemas, stack, out var resolved, out var deeper))
        {
            foreach (var member in DateMembersOf(resolved, schemas, deeper, path))
            {
                yield return member;
            }

            yield break;
        }

        foreach (var keyword in new[] { "anyOf", "oneOf", "allOf" })
        {
            if (schema.TryGetProperty(keyword, out var alternatives))
            {
                foreach (var member in alternatives.EnumerateArray().SelectMany(alternative => DateMembersOf(alternative, schemas, stack, path)))
                {
                    yield return member;
                }
            }
        }

        if (schema.TryGetProperty("properties", out var properties))
        {
            foreach (var property in properties.EnumerateObject())
            {
                var name = path.Length == 0 ? property.Name : $"{path}.{property.Name}";

                if (IsDate(property.Value))
                {
                    yield return name;
                    continue;
                }

                foreach (var member in DateMembersOf(property.Value, schemas, stack, name))
                {
                    yield return member;
                }
            }
        }

        if (schema.TryGetProperty("items", out var items))
        {
            foreach (var member in DateMembersOf(items, schemas, stack, path + "[]"))
            {
                yield return member;
            }
        }

        if (schema.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind == JsonValueKind.Object)
        {
            foreach (var member in DateMembersOf(additional, schemas, stack, path + "{}"))
            {
                yield return member;
            }
        }
    }

    /// <summary>Follows a <c>$ref</c>; a schema already on the way down is a cycle and is not entered again.</summary>
    private static bool Resolve(JsonElement schema, JsonElement schemas, IReadOnlyList<string> stack, out JsonElement resolved, out IReadOnlyList<string> deeper)
    {
        resolved = default;
        deeper = stack;

        if (!schema.TryGetProperty("$ref", out var reference))
        {
            return false;
        }

        var text = reference.GetString()!;
        var name = text[(text.LastIndexOf('/') + 1)..];

        if (stack.Contains(name, StringComparer.Ordinal) || !schemas.TryGetProperty(name, out resolved))
        {
            // A cycle holds no date the first visit did not report; an unknown reference is not ours to judge.
            resolved = JsonSerializer.SerializeToElement(new { });
        }

        deeper = [.. stack, name];

        return true;
    }
}
