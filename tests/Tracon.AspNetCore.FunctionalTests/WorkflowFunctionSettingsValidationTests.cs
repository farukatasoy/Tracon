using System.Net;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.WorkflowSettingsFixtures;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// What <c>PUT /api/workflows/{name}</c> refuses about a node's settings: the object
/// must fit the settings type the function declares, and the answer names the node and the member.
/// </summary>
/// <remarks>
/// The check is the strict read of the object into the function's settings type, done where the
/// registry is - so the operator hears about a wrong setting when saving, not in the first run.
/// </remarks>
public sealed class WorkflowFunctionSettingsValidationTests
{
    [Theory]
    [InlineData("""{"voice":"a","voise":"b"}""", "'voise'")]
    [InlineData("""{"voice":"a","loudnessLufs":"loud"}""", "$.loudnessLufs")]
    [InlineData("""{"voice":"a","loudnessLufs":"-12"}""", "$.loudnessLufs")]
    [InlineData("""{"voice":null}""", "'voice'")]
    [InlineData("""{"voice":"a","speed":2}""", "$.speed")]
    [InlineData("{}", "'voice'")]
    public async Task A_settings_object_the_type_cannot_hold_is_refused_and_names_the_node_and_member(string settings, string member)
    {
        await using var host = await StartAsync();

        using var response = await SaveAsync(host, "narrated", Chain(settings));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await ProblemOf(response);
        var detail = problem.GetProperty("detail").GetString()!;

        problem.GetProperty("title").GetString().ShouldBe("Workflow definition invalid");
        detail.ShouldContain("Workflow 'narrated'", Case.Sensitive);
        detail.ShouldContain("Function node 'tts' has invalid settings", Case.Sensitive);
        detail.ShouldContain(member, Case.Sensitive);

        // Refused means NOT stored.
        using var read = await host.Client.GetAsync("/tracon/api/workflows/narrated");

        read.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_node_with_no_settings_is_refused_when_the_type_has_a_required_member()
    {
        // Absent settings are read as {}: the operator hears it now, not at the first run.
        await using var host = await StartAsync();

        using var response = await SaveAsync(host, "narrated", Chain(settings: null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemOf(response)).GetProperty("detail").GetString()!.ShouldContain("'voice'", Case.Sensitive);
    }

    [Theory]
    [InlineData("[1,2]", "array")]
    [InlineData("\"loud\"", "string")]
    [InlineData("3", "number")]
    public async Task Settings_that_are_not_a_JSON_object_are_refused(string settings, string kind)
    {
        await using var host = await StartAsync();

        using var response = await SaveAsync(host, "narrated", Chain(settings));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var detail = (await ProblemOf(response)).GetProperty("detail").GetString()!;

        detail.ShouldContain("Node 'tts'", Case.Sensitive);
        detail.ShouldContain("JSON object", Case.Sensitive);
        detail.ShouldContain(kind, Case.Sensitive);
    }

    [Fact]
    public async Task Settings_on_an_agent_node_are_refused()
    {
        await using var host = await StartAsync();

        using var response = await SaveAsync(
            host,
            "narrated",
            [Node(Writer, "Agent", """{"tone":"formal"}"""), Node(Tts, "Function", """{"voice":"alloy"}""")]);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var detail = (await ProblemOf(response)).GetProperty("detail").GetString()!;

        detail.ShouldContain("Node 'writer'", Case.Sensitive);
        detail.ShouldContain("agent node", Case.Sensitive);
    }

    [Fact]
    public async Task Settings_on_a_function_that_declares_none_are_refused()
    {
        // The function takes the chat message list the agent node before it passes on.
        await using var host = await StartAsync(static builder => builder
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("plain", static _ => static (messages, _, _) => new ValueTask<List<ChatMessage>>(messages)));

        using var refused = await SaveAsync(host, "chain", Chain("""{"voice":"alloy"}""", "plain"));

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemOf(refused)).GetProperty("detail").GetString()!.ShouldContain("declares none", Case.Sensitive);

        // Without settings the same node is fine.
        using var accepted = await SaveAsync(host, "chain", [Node(Writer, "Agent"), Node("plain", "Function")]);

        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_setter_in_the_settings_type_that_throws_is_a_400_naming_the_kind_of_refusal_never_a_500()
    {
        // The settings type is the consumer's code; a range check in a setter refuses a value by throwing.
        // The operator typed it, so it is theirs to fix - and its message, which can carry the value, stays out.
        await using var host = await StartAsync(static builder => builder
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>, ShortVoiceSettings>(
                "short",
                static _ => static (messages, _, _, _) => new ValueTask<List<ChatMessage>>(messages)));

        using var response = await SaveAsync(host, "narrated", Chain("""{"voice":"abcdefgh"}""", "short"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var detail = (await ProblemOf(response)).GetProperty("detail").GetString()!;

        detail.ShouldContain("Function node 'short' has invalid settings", Case.Sensitive);
        detail.ShouldContain("rejected a value (ArgumentException)", Case.Sensitive);
        detail.ShouldNotContain("too long", Case.Sensitive);
        detail.ShouldNotContain("abcdefgh", Case.Sensitive);

        using var accepted = await SaveAsync(host, "narrated", Chain("""{"voice":"abc"}""", "short"));

        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_empty_settings_object_is_accepted_when_nothing_is_required()
    {
        // 16: the empty object is a legitimate input, not an error.
        await using var host = await StartAsync(static builder => builder
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>, MixSettings>(
                "mix",
                static _ => static (messages, settings, _, _) => Say($"preset={settings.Preset}")));

        await SaveOkAsync(host, "mixed", [Node(Writer, "Agent"), Node("mix", "Function", "{}")]);

        (await RunAsync(host, "mixed")).Output.ShouldBe("preset=flat");
    }

    [Fact]
    public async Task Settings_over_the_size_limit_are_refused_with_the_limit_named()
    {
        // 16: the limit keeps the definition - written whole on every save, read on every run, listed with every
        // workflow - from becoming a data store.
        await using var host = await StartAsync(static builder => builder.UseWorkflows(static options => options.MaxNodeSettingsLength = 64));

        await SaveOkAsync(host, "small", Chain("""{"voice":"alloy"}"""));

        using var response = await SaveAsync(host, "large", Chain($$"""{"voice":"{{new string('v', 200)}}"}"""));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var detail = (await ProblemOf(response)).GetProperty("detail").GetString()!;

        detail.ShouldContain("larger than 64 bytes", Case.Sensitive);
        detail.ShouldContain("MaxNodeSettingsLength", Case.Sensitive);
    }

    [Fact]
    public async Task The_default_size_limit_refuses_a_very_large_blob()
    {
        await using var host = await StartAsync();

        using var response = await SaveAsync(host, "large", Chain($$"""{"voice":"{{new string('v', 20 * 1024)}}"}"""));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemOf(response)).GetProperty("detail").GetString()!.ShouldContain("larger than 16384 bytes", Case.Sensitive);
    }

    internal sealed class ShortVoiceSettings
    {
        public required string Voice
        {
            get;
            init => field = value.Length <= 5 ? value : throw new ArgumentException($"Voice '{value}' is too long", nameof(value));
        }
    }
}
