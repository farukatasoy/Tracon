using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 201 (F-302), failure modes 9 and 11: <c>POST /api/agents/validate</c>
/// reports EVERY loop problem with its own code and path, and the save path's
/// <c>400</c> carries all of them.
/// </summary>
/// <remarks>
/// Before this phase the compiler stopped at the first loop problem, and the
/// report carried that one as a path-less <c>compilation_error</c>: an operator
/// fixed one field per round trip and could not tell which criterion was meant.
/// </remarks>
public sealed class AgentValidationLoopTests
{
    private static readonly Uri Validate = new("/tracon/api/agents/validate", UriKind.Relative);
    private static readonly Uri Agents = new("/tracon/api/agents", UriKind.Relative);

    [Fact]
    public async Task Three_loop_problems_are_three_messages_with_codes_and_paths()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Validate, ThreeProblems());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await TraconTestHost.ReadJsonAsync(response);
        json.GetProperty("valid").GetBoolean().ShouldBeFalse();

        Messages(json).ShouldBe(
        [
            ("loop_max_iterations_invalid", "harness.loop.maxIterations"),
            ("loop_marker_missing", "harness.loop.criteria[0].marker"),
            ("loop_criterion_unknown", "harness.loop.criteria[1].kind"),
        ]);
    }

    [Fact]
    public async Task Saving_the_same_definition_is_refused_with_every_loop_problem()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Agents, ThreeProblems());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var detail = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString().ShouldNotBeNull();
        detail.ShouldContain("MaxIterations");
        detail.ShouldContain("'Marker'");
        detail.ShouldContain("'x'");
    }

    [Theory]
    [InlineData("{\"criteria\":[]}", "loop_criteria_empty", "harness.loop.criteria")]
    [InlineData("{\"criteria\":[{\"kind\":\"\"}]}", "loop_criterion_kind_missing", "harness.loop.criteria[0].kind")]
    [InlineData("{\"criteria\":[{\"kind\":\"   \"}]}", "loop_criterion_kind_missing", "harness.loop.criteria[0].kind")]
    [InlineData("{\"criteria\":[{\"kind\":\"completionMarker\",\"marker\":\"DONE\"}],\"maxIterations\":0}", "loop_max_iterations_invalid", "harness.loop.maxIterations")]
    [InlineData("{\"criteria\":[{\"kind\":\"aiJudge\",\"judgeCriteria\":[\"\"]}]}", "loop_judge_criteria_missing", "harness.loop.criteria[0].judgeCriteria[0]")]
    [InlineData("{\"criteria\":[{\"kind\":\"aiJudge\",\"judgeCriteria\":[]}]}", "loop_judge_criteria_missing", "harness.loop.criteria[0].judgeCriteria")]
    [InlineData("{\"criteria\":[{\"kind\":\"aiJudge\",\"judgeCriteria\":[\"ok?\",\"  \"]}]}", "loop_judge_criteria_missing", "harness.loop.criteria[0].judgeCriteria[1]")]
    public async Task Empty_and_out_of_range_loop_input_is_reported_at_its_path(string loop, string code, string path)
    {
        await using var host = await TraconTestHost.StartAsync();

        var json = await ValidateAsync(host, loop);

        json.GetProperty("valid").GetBoolean().ShouldBeFalse();
        Messages(json).ShouldContain((code, path));
    }

    [Fact]
    public async Task The_largest_iteration_ceiling_is_valid_even_when_exhaustion_fails_the_run()
    {
        // FailRun hands the framework N + 1; int.MaxValue must not overflow it.
        await using var host = await TraconTestHost.StartAsync();

        var json = await ValidateAsync(
            host,
            "{\"criteria\":[{\"kind\":\"completionMarker\",\"marker\":\"DONE\"}],\"maxIterations\":2147483647,\"onExhausted\":\"FailRun\"}");

        json.GetProperty("valid").GetBoolean().ShouldBeTrue(json.ToString());
    }

    [Fact]
    public async Task A_criterion_without_a_kind_property_is_a_request_error()
    {
        // Kind is required in the contract; a body without it cannot bind.
        await using var host = await TraconTestHost.StartAsync();

        using var response = await PostValidateAsync(host, "{\"criteria\":[{}]}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_aiJudge_on_a_host_without_a_judge_model_is_reported_at_the_criterion()
    {
        await using var host = await TraconTestHost.StartAsync();

        var json = await ValidateAsync(host, "{\"criteria\":[{\"kind\":\"completionMarker\",\"marker\":\"DONE\"},{\"kind\":\"aiJudge\",\"judgeCriteria\":[\"Complete?\"]}]}");

        Messages(json).ShouldBe([("loop_judge_unconfigured", "harness.loop.criteria[1].kind")]);
    }

    [Fact]
    public async Task An_aiJudge_whose_judge_model_cannot_be_built_is_reported_at_the_criterion()
    {
        // The judge binding names a provider nobody registered: the same tenant-aware
        // member the compiler calls refuses it, and the report says where.
        await using var host = await TraconTestHost.StartAsync(static builder => builder
            .AddModelRunJudge(static options => options.Model = new ModelBinding { Provider = "absent", Model = "judge-1" }));

        var json = await ValidateAsync(host, "{\"criteria\":[{\"kind\":\"aiJudge\",\"judgeCriteria\":[\"Complete?\"]}]}");

        json.GetProperty("valid").GetBoolean().ShouldBeFalse();
        Messages(json).ShouldBe([("loop_judge_unavailable", "harness.loop.criteria[0].kind")]);
    }

    private static object ThreeProblems() => new
    {
        name = "w",
        model = new { provider = "echo", model = "e" },
        harness = new
        {
            loop = new
            {
                criteria = new object[] { new { kind = "completionMarker" }, new { kind = "x" } },
                maxIterations = 0,
            },
        },
    };

    private static async Task<JsonElement> ValidateAsync(TraconTestHost host, string loop)
    {
        using var response = await PostValidateAsync(host, loop);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await TraconTestHost.ReadJsonAsync(response);
    }

    private static Task<HttpResponseMessage> PostValidateAsync(TraconTestHost host, string loop)
        => host.Client.PostAsync(
            Validate,
            new StringContent(
                $"{{\"name\":\"w\",\"model\":{{\"provider\":\"echo\",\"model\":\"e\"}},\"harness\":{{\"loop\":{loop}}}}}",
                System.Text.Encoding.UTF8,
                "application/json"));

    private static List<(string? Code, string? Path)> Messages(JsonElement report)
        => report.GetProperty("messages").EnumerateArray()
            .Select(static message => (
                message.GetProperty("code").GetString(),
                message.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String ? path.GetString() : null))
            .ToList();
}
