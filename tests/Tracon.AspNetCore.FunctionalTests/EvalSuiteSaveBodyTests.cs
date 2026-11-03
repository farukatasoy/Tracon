using System.Net;
using System.Text;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// <c>checks</c> of an eval suite is optional: left out, <c>null</c> and <c>[]</c> all save a suite with no checks, and
/// it reads back as the empty array. A value that is not an array is refused.
/// </summary>
/// <remarks>
/// <para>
/// The generated clients mark the member optional and nullable, so a caller that sets only <c>agentName</c> sends
/// either nothing or <c>null</c>. Both reach the same place: the suite keeps the empty array, which every store accepts
/// (a literal JSON <c>null</c> is refused by SQL Server's JSON column check), so the answer is the same on the
/// in-memory store and on a SQL store. SQLite stands in for the SQL stores here; the store contract tests cover the
/// other two.
/// </para>
/// </remarks>
public sealed class EvalSuiteSaveBodyTests
{
    public static TheoryData<string, string, string> Bodies => new()
    {
        { "memory", """{"agentName":"kod-agent"}""", "[]" },
        { "memory", """{"agentName":"kod-agent","checks":null}""", "[]" },
        { "memory", """{"agentName":"kod-agent","checks":[]}""", "[]" },
        { "memory", """{"agentName":"kod-agent","checks":[{"kind":"nonEmpty"}]}""", """[{"kind":"nonEmpty"}]""" },
        { "sqlite", """{"agentName":"kod-agent"}""", "[]" },
        { "sqlite", """{"agentName":"kod-agent","checks":null}""", "[]" },
        { "sqlite", """{"agentName":"kod-agent","checks":[]}""", "[]" },
        { "sqlite", """{"agentName":"kod-agent","checks":[{"kind":"nonEmpty"}]}""", """[{"kind":"nonEmpty"}]""" },
    };

    [Theory]
    [MemberData(nameof(Bodies))]
    public async Task A_suite_is_saved_and_reads_back_with_the_checks_it_was_given_or_none(string store, string body, string expectedChecks)
    {
        using var database = new TempSqliteDatabase("eval-body");
        await using var host = await StartAsync(store, database);

        using var saved = await host.Client.PutAsync(SuiteUri, Json(body));

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        (await TraconTestHost.ReadJsonAsync(saved)).GetProperty("checks").GetRawText().ShouldBe(expectedChecks);

        using var read = await host.Client.GetAsync(SuiteUri);

        (await TraconTestHost.ReadJsonAsync(read)).GetProperty("checks").GetRawText().ShouldBe(expectedChecks);
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task A_checks_value_that_is_not_an_array_is_refused_with_a_message_that_names_it(string store)
    {
        using var database = new TempSqliteDatabase("eval-body");
        await using var host = await StartAsync(store, database);

        using var refused = await host.Client.PutAsync(SuiteUri, Json("""{"agentName":"kod-agent","checks":5}"""));

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("must be a JSON array", Case.Sensitive);
    }

    private static Uri SuiteUri { get; } = new("/tracon/api/evals/body-suite", UriKind.Relative);

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static Task<TraconTestHost> StartAsync(string store, TempSqliteDatabase database)
        => TraconTestHost.StartAsync(builder =>
        {
            if (string.Equals(store, "sqlite", StringComparison.Ordinal))
            {
                builder.UseSqlite(database.ConnectionString);
            }

            builder.AddAgent(TestData.Definition());
        });
}
