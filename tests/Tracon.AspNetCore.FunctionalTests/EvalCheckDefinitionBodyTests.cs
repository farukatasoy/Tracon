using System.Net;
using System.Text;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A check definition in an eval suite that is not shaped the way the registry reads it is a 400 that says so.
/// </summary>
/// <remarks>
/// The registry read each definition with <c>JsonElement</c> accessors that throw on a value of the wrong kind
/// (<c>TryGetProperty</c> on an array, <c>GetInt32</c> on 1.5, <c>GetString</c> on a number), and nothing between the
/// registry and the caller catches an <see cref="InvalidOperationException"/> or a <see cref="FormatException"/>: the
/// suite save answered 500. The endpoint already turns a <see cref="TraconException"/> into the 400 an unknown kind gets.
/// </remarks>
public sealed class EvalCheckDefinitionBodyTests
{
    [Theory]
    [InlineData("[1]")]
    [InlineData("""["nonEmpty"]""")]
    [InlineData("[[]]")]
    [InlineData("[null]")]
    [InlineData("""[{"kind":"nonEmpty","minLength":1.5}]""")]
    [InlineData("""[{"kind":"nonEmpty","minLength":99999999999}]""")]
    [InlineData("""[{"kind":"keywords","values":[1]}]""")]
    [InlineData("""[{"kind":"keywords","values":[null]}]""")]
    [InlineData("""[{"kind":"toolCalled","tools":[{}]}]""")]
    public async Task A_check_definition_the_registry_cannot_read_is_refused_with_400(string checks)
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder.AddAgent(TestData.Definition("writer")));

        using var content = new StringContent($$"""{"agentName":"writer","checks":{{checks}}}""", Encoding.UTF8, "application/json");
        using var response = await host.Client.PutAsync(new Uri("/tracon/api/evals/probe", UriKind.Relative), content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_suite_saved_with_null_checks_has_no_checks_and_reads_back_as_the_empty_array()
    {
        // `null` saved on the in-memory, SQLite and PostgreSQL stores and was a CHECK (ISJSON) violation, reported as
        // "the store is unavailable" (503), on SQL Server. The empty array is what "no checks" is everywhere else.
        await using var host = await TraconTestHost.StartAsync(static builder => builder.AddAgent(TestData.Definition("writer")));

        using var content = new StringContent("""{"agentName":"writer","checks":null}""", Encoding.UTF8, "application/json");
        using var saved = await host.Client.PutAsync(new Uri("/tracon/api/evals/nullchecks", UriKind.Relative), content);

        saved.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var read = await host.Client.GetAsync(new Uri("/tracon/api/evals/nullchecks", UriKind.Relative));
        var suite = await TraconTestHost.ReadJsonAsync(read);

        suite.GetProperty("checks").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Array);
        suite.GetProperty("checks").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_well_formed_check_definition_is_still_saved()
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder.AddAgent(TestData.Definition("writer")));

        using var content = new StringContent(
            """{"agentName":"writer","checks":[{"kind":"nonEmpty","minLength":2},{"kind":"keywords","values":["a","b"],"caseSensitive":true}]}""",
            Encoding.UTF8,
            "application/json");
        using var response = await host.Client.PutAsync(new Uri("/tracon/api/evals/probe", UriKind.Relative), content);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
