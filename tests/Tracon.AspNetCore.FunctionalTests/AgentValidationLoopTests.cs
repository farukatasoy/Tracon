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
    [InlineData("{\"criteria\":[{\"kind\":\"agentReview\"}]}", "loop_reviewer_missing", "harness.loop.criteria[0].reviewerAgent")]
    [InlineData("{\"criteria\":[{\"kind\":\"agentReview\",\"reviewerAgent\":\"  \"}]}", "loop_reviewer_missing", "harness.loop.criteria[0].reviewerAgent")]
    [InlineData("{\"criteria\":[{\"kind\":\"completionMarker\",\"marker\":\"DONE\"},{\"kind\":\"agentReview\",\"reviewerAgent\":\"w\"}]}", "loop_reviewer_self", "harness.loop.criteria[1].reviewerAgent")]
    [InlineData("{\"criteria\":[{\"kind\":\"agentReview\",\"reviewerAgent\":\"other\",\"reviewerAgentVersion\":0}]}", "loop_reviewer_version_invalid", "harness.loop.criteria[0].reviewerAgentVersion")]
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

    // ---- Phase 242 (F-411): the catalog step for an agentReview criterion's reviewer ----

    [Fact]
    public async Task A_reviewer_that_is_not_in_the_catalog_is_reported_at_the_criterion_and_saving_is_refused()
    {
        await using var host = await TraconTestHost.StartAsync();

        const string Loop = "{\"criteria\":[{\"kind\":\"agentReview\",\"reviewerAgent\":\"no-such-reviewer\"}]}";

        var json = await ValidateAsync(host, Loop);

        json.GetProperty("valid").GetBoolean().ShouldBeFalse();
        Messages(json).ShouldBe([("loop_reviewer_not_found", "harness.loop.criteria[0].reviewerAgent")]);

        // The save path runs the same validator: an agent cannot be stored with a reviewer nobody has.
        using var save = await host.Client.PostAsync(
            Agents,
            new StringContent(
                $"{{\"name\":\"w\",\"model\":{{\"provider\":\"echo\",\"model\":\"e\"}},\"harness\":{{\"loop\":{Loop}}}}}",
                System.Text.Encoding.UTF8,
                "application/json"));

        save.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TraconTestHost.ReadJsonAsync(save)).GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("no-such-reviewer");
    }

    [Fact]
    public async Task A_reviewer_defined_in_code_cannot_be_pinned_to_a_version()
    {
        await using var host = await StartWithReviewerAsync(verdictSchema: false);

        var json = await ValidateAsync(
            host, "{\"criteria\":[{\"kind\":\"agentReview\",\"reviewerAgent\":\"code-reviewer\",\"reviewerAgentVersion\":2}]}");

        json.GetProperty("valid").GetBoolean().ShouldBeFalse();
        Messages(json).ShouldBe([("loop_reviewer_version_invalid", "harness.loop.criteria[0].reviewerAgentVersion")]);
    }

    [Fact]
    public async Task A_reviewer_without_the_verdict_schema_is_a_warning_and_the_definition_stays_valid()
    {
        await using var host = await StartWithReviewerAsync(verdictSchema: false);

        var json = await ValidateAsync(host, "{\"criteria\":[{\"kind\":\"agentReview\",\"reviewerAgent\":\"code-reviewer\"}]}");

        // A warning, not an error: the reviewer may change before the loop runs, and an answer that is not the
        // verdict is a criterion failure at run time either way.
        json.GetProperty("valid").GetBoolean().ShouldBeTrue(json.ToString());

        var warning = json.GetProperty("messages").EnumerateArray().Single();
        warning.GetProperty("severity").ToString().ShouldBeOneOf("Warning", "2");
        warning.GetProperty("code").GetString().ShouldBe("loop_reviewer_verdict_unknown");
        warning.GetProperty("path").GetString().ShouldBe("harness.loop.criteria[0].reviewerAgent");
    }

    [Fact]
    public async Task A_reviewer_with_the_verdict_schema_has_nothing_to_report()
    {
        await using var host = await StartWithReviewerAsync(verdictSchema: true);

        var json = await ValidateAsync(host, "{\"criteria\":[{\"kind\":\"agentReview\",\"reviewerAgent\":\"code-reviewer\"}]}");

        json.GetProperty("valid").GetBoolean().ShouldBeTrue(json.ToString());
        json.GetProperty("messages").GetArrayLength().ShouldBe(0, json.ToString());
    }

    [Fact]
    public async Task A_stored_reviewer_pinned_to_a_version_it_does_not_have_is_not_found_at_the_pin()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var created = await host.Client.PostAsJsonAsync(
            Agents,
            new { name = "stored-reviewer", model = new { provider = "echo", model = "e" } });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Version 1 exists; version 5 does not.
        var present = await ValidateAsync(
            host, "{\"criteria\":[{\"kind\":\"agentReview\",\"reviewerAgent\":\"stored-reviewer\",\"reviewerAgentVersion\":1}]}");
        Messages(present).Select(static message => message.Code).ShouldNotContain("loop_reviewer_not_found", StringComparer.Ordinal);

        var absent = await ValidateAsync(
            host, "{\"criteria\":[{\"kind\":\"agentReview\",\"reviewerAgent\":\"stored-reviewer\",\"reviewerAgentVersion\":5}]}");

        absent.GetProperty("valid").GetBoolean().ShouldBeFalse();
        Messages(absent).ShouldContain(("loop_reviewer_not_found", "harness.loop.criteria[0].reviewerAgentVersion"));
    }

    [Fact]
    public async Task The_reviewer_problems_come_with_the_other_loop_problems_in_one_report()
    {
        await using var host = await TraconTestHost.StartAsync();

        // The structural problems come first and stop the catalog step: a report that looked the reviewer up
        // in a catalog while its name was empty would say two things about one field.
        var json = await ValidateAsync(
            host,
            "{\"criteria\":[{\"kind\":\"completionMarker\"},{\"kind\":\"agentReview\"}],\"maxIterations\":0}");

        Messages(json).ShouldBe(
        [
            ("loop_max_iterations_invalid", "harness.loop.maxIterations"),
            ("loop_marker_missing", "harness.loop.criteria[0].marker"),
            ("loop_reviewer_missing", "harness.loop.criteria[1].reviewerAgent"),
        ]);
    }

    private static Task<TraconTestHost> StartWithReviewerAsync(bool verdictSchema)
        => TraconTestHost.StartAsync(builder => builder.AddAgent(new AgentDefinition
        {
            Name = "code-reviewer",
            Description = "Reviews drafts.",
            Instructions = "Review the draft.",
            Model = new ModelBinding
            {
                Provider = "echo",
                Model = "e",
                ResponseFormat = verdictSchema
                    ? new AgentResponseFormat
                    {
                        Kind = AgentResponseFormatKind.JsonSchema,
                        SchemaName = "verdict",
                        Schema = JsonDocument.Parse(
                            """{"type":"object","properties":{"approved":{"type":"boolean"},"feedback":{"type":"string"}},"required":["approved"]}""")
                            .RootElement.Clone(),
                    }
                    : null,
            },
            Origin = AgentDefinitionOrigin.Code,
        }));

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
