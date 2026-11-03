using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// An enum-typed field of a request body that carries a number naming no member is refused with <c>400</c> and the
/// message that lists the members; a number that names a member is still accepted.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The defect: the converter that reads enum fields of a body accepts an integer (it delegates to the framework's
/// converter, which does), and an integer that is no member became an enum value that has no name.
/// <c>POST /api/runs/{runId}/feedback</c> with <c>"kind": 0</c> answered <c>200</c> and stored a score of kind
/// <c>0</c>, because <c>RunScoreKind</c> starts at <c>1</c>. The next read wrote <c>"kind": 0</c> where the document says
/// the field is one of four names. Every list that held that score then broke its own schema; the Schemathesis run found
/// it on three operations once the schema check joined the gate.
/// </para>
/// <para>
/// The fix sits on the request-body options, so it covers every enum field of every contract. The tests pick a field on
/// another endpoint on purpose.
/// </para>
/// </remarks>
public sealed class UndefinedEnumNumberBodyTests
{
    private const string Agent = "kod-agent";

    [Theory]
    [InlineData("0")]
    [InlineData("99")]
    [InlineData("\"0\"")]
    public async Task A_score_kind_that_names_no_member_is_refused_and_nothing_is_stored(string kind)
    {
        await using var host = await StartAsync();
        var runId = await SeedRunAsync(host);

        using var response = await PostFeedbackAsync(host, runId, $$"""{"kind":{{kind}},"value":0}""");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var detail = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString()!;

        detail.ShouldContain("is not a valid value.", Case.Sensitive);
        detail.ShouldContain("Binary, Stars, Numeric, Categorical", Case.Sensitive);
        detail.ShouldContain("$.kind", Case.Sensitive);

        using var listed = await host.Client.GetAsync(new Uri($"/tracon/api/runs/{runId}/feedback", UriKind.Relative));
        var scores = await TraconTestHost.ReadJsonAsync(listed);

        scores.GetArrayLength().ShouldBe(0, "a refused score must not be stored");
    }

    [Fact]
    public async Task A_score_kind_written_as_the_number_of_a_member_is_still_accepted()
    {
        await using var host = await StartAsync();
        var runId = await SeedRunAsync(host);

        using var response = await PostFeedbackAsync(host, runId, """{"kind":1,"value":1}""");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("kind").GetString().ShouldBe("Binary");
    }

    [Fact]
    public async Task A_number_that_names_no_member_is_refused_on_another_endpoint_too()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsync(
            new Uri("/tracon/api/agents/validate", UriKind.Relative),
            new StringContent(
                """{"name":"c5","model":{"provider":"echo","model":"echo-1"},"compaction":{"strategy":99,"triggerTokens":1000}}""",
                Encoding.UTF8,
                "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString()!
            .ShouldContain("SlidingWindow", Case.Sensitive);
    }

    private static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(static builder => builder.AddAgent(TestData.Definition()));

    private static async Task<Guid> SeedRunAsync(TraconTestHost host)
    {
        using var run = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/agents/{Agent}/run", UriKind.Relative),
            new { message = "seed" },
            TestContext.Current.CancellationToken);

        run.EnsureSuccessStatusCode();

        var runs = await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken);

        return runs[0].Id;
    }

    private static Task<HttpResponseMessage> PostFeedbackAsync(TraconTestHost host, Guid runId, string body)
        => host.Client.PostAsync(
            new Uri($"/tracon/api/runs/{runId}/feedback", UriKind.Relative),
            new StringContent(body, Encoding.UTF8, "application/json"));
}
