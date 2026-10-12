using System.Net;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.WorkflowSettingsFixtures;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A function node's settings over HTTP (F-295): saved with the workflow, read back with
/// it, and handed to the function when the workflow runs.
/// </summary>
/// <remarks>
/// 🚨 This crosses the DI, HTTP, store and run boundaries, so a unit test cannot show it:
/// the property has to survive the request body, the definition store and the compile that
/// every run does again.
/// </remarks>
public sealed class WorkflowFunctionSettingsTests
{
    [Fact]
    public async Task Settings_saved_over_HTTP_are_read_back_and_reach_the_function_when_the_workflow_runs()
    {
        await using var host = await StartAsync();

        await SaveOkAsync(host, "narrated", Chain("""{"voice":"alloy","loudnessLufs":-12.5,"speed":"Fast"}"""));

        var stored = await TraconTestHost.ReadJsonAsync(await host.Client.GetAsync("/tracon/api/workflows/narrated"));
        var settings = stored.GetProperty("nodes")[1].GetProperty("settings");

        settings.GetProperty("voice").GetString().ShouldBe("alloy");
        settings.GetProperty("loudnessLufs").GetDouble().ShouldBe(-12.5);
        stored.GetProperty("nodes")[0].GetProperty("settings").ValueKind.ShouldBe(JsonValueKind.Null);

        var run = await RunAsync(host, "narrated");

        run.Record.Status.ShouldBe(RunStatus.Completed, run.Record.Error?.Message);
        run.Output.ShouldBe("voice=alloy;lufs=-12.5;speed=Fast");
    }

    [Fact]
    public async Task Two_function_nodes_in_one_chain_each_receive_their_own_settings()
    {
        // 1: the settings must reach the node they were written for, not the neighbor.
        await using var host = await StartAsync(static builder => builder
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>, MixSettings>(
                "mix",
                static _ => static (messages, settings, _, _) => Say($"{messages[^1].Text}|preset={settings.Preset}")));

        await SaveOkAsync(
            host,
            "narrated",
            [Node(Writer, "Agent"), Node(Tts, "Function", """{"voice":"alloy"}"""), Node("mix", "Function", """{"preset":"warm"}""")]);

        var run = await RunAsync(host, "narrated");

        run.Record.Status.ShouldBe(RunStatus.Completed, run.Record.Error?.Message);
        run.Output.ShouldBe("voice=alloy;lufs=-16;speed=Normal|preset=warm");
    }

    [Fact]
    public async Task One_function_serves_two_workflows_with_a_different_configuration_in_each()
    {
        await using var host = await StartAsync();

        await SaveOkAsync(host, "host-a", Chain("""{"voice":"alloy"}"""));
        await SaveOkAsync(host, "host-b", Chain("""{"voice":"nova"}"""));

        (await RunAsync(host, "host-a")).Output.ShouldBe("voice=alloy;lufs=-16;speed=Normal");
        (await RunAsync(host, "host-b")).Output.ShouldBe("voice=nova;lufs=-16;speed=Normal");
    }

    [Fact]
    public async Task The_workflow_lists_are_served_when_nodes_with_and_without_settings_sit_side_by_side()
    {
        // 5: a node whose settings element stayed unset (not null) is the one value System.Text.Json
        // cannot write. One such node would fail this whole list - every workflow, not just its own.
        await using var host = await StartAsync(static builder => builder
            .AddWorkflowFunction<string, string>("plain", static _ => static (input, _, _) => new ValueTask<string>(input)));

        await SaveOkAsync(host, "with-settings", Chain("""{"voice":"alloy"}"""));
        await SaveOkAsync(host, "without-settings", [Node(Writer, "Agent"), Node("plain", "Function")]);
        await SaveOkAsync(host, "agents-only", [Node(Writer, "Agent")]);

        using var listed = await host.Client.GetAsync("/tracon/api/workflows");

        listed.StatusCode.ShouldBe(HttpStatusCode.OK, await listed.Content.ReadAsStringAsync());

        var names = (await TraconTestHost.ReadJsonAsync(listed)).EnumerateArray()
            .Select(static item => item.GetProperty("name").GetString())
            .ToList();

        names.ShouldBe(["agents-only", "with-settings", "without-settings"], ignoreOrder: true);

        foreach (var name in names)
        {
            using var one = await host.Client.GetAsync($"/tracon/api/workflows/{name}");

            one.StatusCode.ShouldBe(HttpStatusCode.OK, name);
        }

        using var graph = await host.Client.GetAsync("/tracon/api/workflows/with-settings/graph");

        graph.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_settings_value_of_null_means_the_node_carries_none()
    {
        await using var host = await StartAsync(static builder => builder
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>, MixSettings>(
                "mix",
                static _ => static (messages, settings, _, _) => Say($"preset={settings.Preset}")));

        using var response = await host.Client.PutAsync(
            "/tracon/api/workflows/mixed",
            new StringContent(
                """{"kind":"Sequential","nodes":[{"name":"writer","kind":"Agent","settings":null},{"name":"mix","kind":"Function","settings":null}]}""",
                System.Text.Encoding.UTF8,
                "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        (await RunAsync(host, "mixed")).Output.ShouldBe("preset=flat");
    }

    [Fact]
    public async Task Saving_again_without_settings_clears_them_because_a_save_replaces_the_definition()
    {
        // K-725: PUT is a full replace. A client that does not send the settings back erases them.
        await using var host = await StartAsync(static builder => builder
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>, MixSettings>(
                "mix",
                static _ => static (messages, settings, _, _) => Say($"preset={settings.Preset}")));

        await SaveOkAsync(host, "mixed", [Node(Writer, "Agent"), Node("mix", "Function", """{"preset":"warm"}""")]);
        (await RunAsync(host, "mixed")).Output.ShouldBe("preset=warm");

        await SaveOkAsync(host, "mixed", [Node(Writer, "Agent"), Node("mix", "Function")]);

        (await RunAsync(host, "mixed")).Output.ShouldBe("preset=flat");
    }

    [Fact]
    public async Task A_settings_value_never_reaches_the_log_the_stream_or_the_run_events()
    {
        // 18: a setting is a value the operator typed, and K-059 says it may be one they should not have typed.
        // The function below does NOT echo it, so any trace of it is Tracon writing it down.
        const string Marker = "marker-7f3a91c2d4";

        await using var host = await StartAsync(static builder => builder
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>, TtsSettings>(
                "quiet",
                static _ => static (messages, _, _, _) => new ValueTask<List<ChatMessage>>(messages)));

        await SaveOkAsync(host, "narrated", Chain($$"""{"voice":"{{Marker}}"}""", "quiet"));

        var run = await RunAsync(host, "narrated");

        run.Record.Status.ShouldBe(RunStatus.Completed, run.Record.Error?.Message);

        // A refused save must not echo the value either.
        using var refused = await SaveAsync(host, "refused", Chain($$"""{"voice":"x","note":"{{Marker}}"}""", "quiet"));

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).ShouldNotContain(Marker, Case.Sensitive);

        host.Logs.AllText.ShouldNotContain(Marker, Case.Sensitive);
        string.Concat(run.Frames.Select(static frame => frame.Data)).ShouldNotContain(Marker, Case.Sensitive);

        var events = new List<string>();

        await foreach (var runEvent in host.Services.GetRequiredService<IRunStore>().ReadEventsAsync(run.Record.Id, 0))
        {
            events.Add(JsonSerializer.Serialize(runEvent, JsonSerializerOptions.Web));
        }

        events.ShouldNotBeEmpty();
        string.Concat(events).ShouldNotContain(Marker, Case.Sensitive);
        JsonSerializer.Serialize(run.Record, JsonSerializerOptions.Web).ShouldNotContain(Marker, Case.Sensitive);
    }
}
