using System.Net;
using System.Net.Http.Json;
using System.Text;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// F-605 (phase 227) - a case's <c>expectedTools</c> became <c>expectedToolCalls</c>. The
/// cases endpoint refuses the old key and names the new one, because the JSON reader would
/// otherwise drop it without a word and the case would expect no tool call at all.
/// </summary>
public sealed class EvalCaseLegacyFieldTests
{
    private static readonly Uri Suite = new("/tracon/api/evals/legacy-suite", UriKind.Relative);
    private static readonly Uri Cases = new("/tracon/api/evals/legacy-suite/cases", UriKind.Relative);

    [Fact]
    public async Task The_old_key_is_refused_and_the_answer_names_the_new_one()
    {
        await using var host = await StartAsync();

        using var response = await PutCasesAsync(host, """[{"query":"q","expectedTools":["get_order_status"]}]""");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await TraconTestHost.ReadJsonAsync(response);

        problem.GetProperty("title").GetString().ShouldBe("Removed field");

        var detail = problem.GetProperty("detail").GetString().ShouldNotBeNull();

        detail.ShouldContain("'expectedTools'", Case.Sensitive);
        detail.ShouldContain("expectedToolCalls", Case.Sensitive);
    }

    [Fact]
    public async Task The_old_key_is_refused_on_any_case_of_the_list_whatever_its_letter_case_and_value()
    {
        await using var host = await StartAsync();

        foreach (var json in new[]
                 {
                     """[{"query":"first"},{"query":"second","expectedTools":[]}]""",
                     """[{"query":"q","ExpectedTools":["a"]}]""",
                     """[{"query":"q","expectedTools":null}]""",
                 })
        {
            using var response = await PutCasesAsync(host, json);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, json);
        }

        // Nothing was stored.
        using var listed = await host.Client.GetAsync(Cases);
        (await TraconTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task The_new_key_is_accepted_and_a_look_alike_elsewhere_is_only_an_unknown_field()
    {
        await using var host = await StartAsync();

        using var accepted = await PutCasesAsync(host, """[{"query":"q","expectedToolCalls":[{"name":"get_order_status"}]}]""");

        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());

        using var elsewhere = await PutCasesAsync(host, """[{"query":"q","parameters":{"expectedTools":"x"}}]""");

        elsewhere.StatusCode.ShouldBe(HttpStatusCode.OK, await elsewhere.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_body_that_is_not_a_list_gets_the_binders_answer_not_the_removed_field_answer()
    {
        await using var host = await StartAsync();

        using var response = await PutCasesAsync(host, """{"query":"q","expectedTools":["a"]}""");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var title = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("title").GetString();

        string.Equals(title, "Removed field", StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public async Task An_expected_call_without_a_name_is_refused_and_nothing_is_written()
    {
        await using var host = await StartAsync();

        foreach (var json in new[]
                 {
                     """[{"query":"q","expectedToolCalls":[{"name":" "}]}]""",
                     """[{"query":"q","expectedToolCalls":[{"arguments":{"a":1}}]}]""",
                 })
        {
            using var response = await PutCasesAsync(host, json);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, json);
        }

        using var listed = await host.Client.GetAsync(Cases);
        (await TraconTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(0);
    }

    private static async Task<TraconTestHost> StartAsync()
    {
        var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            Suite,
            new EvalSuiteSaveRequest
            {
                AgentName = "legacy-agent",
                Checks = System.Text.Json.JsonDocument.Parse("""[{"kind":"toolCallArgsMatch"}]""").RootElement,
            });

        response.EnsureSuccessStatusCode();

        return host;
    }

    private static async Task<HttpResponseMessage> PutCasesAsync(TraconTestHost host, string json)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, Cases)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        return await host.Client.SendAsync(request);
    }
}
