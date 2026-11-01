using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A request body cannot carry a null where its contract holds a list or a map of values that are not nullable.
/// </summary>
/// <remarks>
/// <para>
/// <c>RespectNullableAnnotations</c> refuses <c>{"items": null}</c> for a non-nullable property, but it does not look
/// inside the collection: <c>{"items": [null]}</c> reaches the handler as a list holding a null, and the first line
/// that reads an element throws <see cref="NullReferenceException"/>, which the caller sees as a 500. A workflow
/// whose <c>nodes</c> hold a null and an eval suite whose cases are <c>[null]</c> did exactly that. The rule is
/// written once, on the options every manual body read uses, because the contracts hold more than a hundred
/// collections and none of their handlers can be trusted to check each one.
/// </para>
/// <para>
/// A collection that is declared to allow it (<c>string?</c>, <c>int?</c>) keeps its nulls.
/// </para>
/// </remarks>
public sealed class NullCollectionElementTests
{
    [Theory]
    [InlineData("""{"items":[null]}""", "items")]
    [InlineData("""{"items":[{"query":"a"},null]}""", "items")]
    [InlineData("""{"names":["a",null]}""", "names")]
    [InlineData("""{"map":{"a":null}}""", "map")]
    public async Task A_null_in_a_collection_of_non_nullable_elements_is_a_400_naming_the_property(string body, string property)
    {
        var (value, error) = await ReadAsync<Holder>(body);

        value.ShouldBeNull();
        error.ShouldNotBeNull();
        error.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        error.ProblemDetails.Detail.ShouldNotBeNull().ShouldContain(property, Case.Sensitive);
        error.ProblemDetails.Detail.ShouldContain("null", Case.Sensitive);
    }

    [Fact]
    public async Task A_null_in_a_constructor_bound_collection_is_refused_too()
    {
        var (value, error) = await ReadAsync<Positional>("""{"items":[null]}""");

        value.ShouldBeNull();
        error.ShouldNotBeNull().StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task A_collection_declared_to_hold_nulls_keeps_them()
    {
        var (value, error) = await ReadAsync<Holder>("""{"optional":["a",null],"numbers":[1,null],"items":[{"query":"q"}]}""");

        error.ShouldBeNull();
        value.ShouldNotBeNull();
        value.Optional.Count.ShouldBe(2);
        value.Optional[1].ShouldBeNull();
        value.Numbers[1].ShouldBeNull();
        value.Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_body_that_is_a_list_with_a_null_in_it_is_a_400()
    {
        var (value, error) = await ReadAsync<IReadOnlyList<Item>>("""[{"query":"a"},null]""");

        value.ShouldBeNull();
        error.ShouldNotBeNull().StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        error.ProblemDetails.Detail.ShouldNotBeNull().ShouldContain("null", Case.Sensitive);
    }

    [Fact]
    public async Task An_optional_body_with_a_null_in_a_collection_is_a_400_too()
    {
        var (value, error) = await ReadAsync<Holder>("""{"items":[null]}""", optional: true);

        value.ShouldBeNull();
        error.ShouldNotBeNull().StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task A_workflow_whose_nodes_hold_a_null_is_refused_with_400_instead_of_failing()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await PutJsonAsync(
            host,
            "/tracon/api/workflows/broken",
            """{"kind":"Sequential","agentNames":[],"nodes":[null]}""");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("nodes", Case.Sensitive);
    }

    [Fact]
    public async Task An_eval_suite_whose_cases_are_a_list_holding_a_null_is_refused_with_400_instead_of_failing()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await PutJsonAsync(host, "/tracon/api/evals/broken/cases", "[null]");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task<HttpResponseMessage> PutJsonAsync(TraconTestHost host, string route, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        return await host.Client.PutAsync(new Uri(route, UriKind.Relative), content);
    }

    private static async Task<(T? Value, ProblemHttpResult? Error)> ReadAsync<T>(string json, bool optional = false)
    {
        var services = new ServiceCollection()
            .AddOptions()
            .Configure<JsonOptions>(static options => options.SerializerOptions.PropertyNameCaseInsensitive = true)
            .BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));

        return optional
            ? await RequestBodyBinding.ReadOptionalAsync<T>(context, CancellationToken.None)
            : await RequestBodyBinding.ReadAsync<T>(context, CancellationToken.None);
    }

    private sealed class Item
    {
        public string Query { get; init; } = "";
    }

    private sealed class Holder
    {
        public IReadOnlyList<Item> Items { get; init; } = [];

        public string[] Names { get; init; } = [];

        public IReadOnlyDictionary<string, Item> Map { get; init; } = new Dictionary<string, Item>(StringComparer.Ordinal);

        public IReadOnlyList<string?> Optional { get; init; } = [];

        public IReadOnlyList<int?> Numbers { get; init; } = [];
    }

    private sealed record Positional(IReadOnlyList<Item> Items);
}
