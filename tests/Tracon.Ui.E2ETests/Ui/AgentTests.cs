using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;
using static Tracon.Ui.E2ETests.Infrastructure.UiTestHelpers;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// The agents screens: the list, the editor, versions and the diff.
/// </summary>
public sealed class AgentTests(BrowserFixture browsers)
{
    [Fact]
    public async Task Code_defined_agent_appears_in_list_and_cannot_be_edited()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/agents");

        await Expect(session.Page.GetByText("Support assistant")).ToBeVisibleAsync();

        await session.Page.GetByText("Support assistant").ClickAsync();

        // Write endpoints return 409 for a code-defined agent; the UI must
        // never show the edit button.
        await Expect(session.Page.GetByText("This agent is declared in code")).ToBeVisibleAsync();

        await Expect(session.Page.GetByRole(AriaRole.Link, new() { Name = "Edit" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Navigating_directly_to_code_agent_edit_URL_fills_form_with_name()
    {
        // BUG-S4-010: the list screen hides "Edit" (per task K1), but
        // navigating directly to the edit URL opened the form empty with a
        // read-only "Name" field — the Validate/Save buttons could never be
        // enabled, so the expected 409 flow for the case was unreachable.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/agents/support/edit");

        var name = session.Page.GetByTestId("agent-name");

        await Expect(name).ToHaveValueAsync("support");
        (await name.IsEditableAsync()).ShouldBeFalse("The code agent's name must still not be editable.");

        await Expect(session.Page.GetByTestId("agent-validate"), "The Validate button stayed disabled even though the form is filled.").ToBeEnabledAsync();
        await Expect(session.Page.GetByTestId("agent-save"), "The Save button stayed disabled even though the form is filled.").ToBeEnabledAsync();
    }

    [Fact]
    public async Task Agent_created_from_UI_can_be_run_immediately()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/agents/new");
        await WaitForDefaultProviderAsync(session.Page);

        await session.Page.GetByTestId("agent-name").FillAsync("ui-agent");
        await session.Page.GetByTestId("agent-display-name").FillAsync("Console agent");
        await session.Page.GetByTestId("agent-model").FillAsync(ScriptedModels.Default);

        await session.Page.GetByTestId("agent-save").ClickAsync();

        // After saving, the detail screen opens and the new definition appears.
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Console agent" })).ToBeVisibleAsync();

        await session.Page.GotoAsync($"{host.UiAddress}/playground/ui-agent");
        await session.Page.GetByTestId("playground-input").FillAsync("hello");
        await session.Page.GetByTestId("playground-send").ClickAsync();

        await Expect(session.Page.GetByText("Echo: hello")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Fallback_list_is_saved_and_read_back()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/agents/new");
        await WaitForDefaultProviderAsync(session.Page);

        await session.Page.GetByTestId("agent-name").FillAsync("fallback-agent");
        await session.Page.GetByTestId("agent-model").FillAsync(ScriptedModels.Default);

        await session.Page.GetByTestId("add-fallback").ClickAsync();
        await session.Page.GetByTestId("fallback-provider-0").SelectOptionAsync(ScriptedModels.ProviderName);
        await session.Page.GetByTestId("fallback-model-0").FillAsync(ScriptedModels.Support);

        await session.Page.GetByTestId("agent-save").ClickAsync();

        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "fallback-agent" })).ToBeVisibleAsync();

        await session.Page.GetByRole(AriaRole.Link, new() { Name = "Edit" }).ClickAsync();

        await Expect(session.Page.GetByTestId("fallback-provider-0")).ToHaveValueAsync(ScriptedModels.ProviderName);
        await Expect(session.Page.GetByTestId("fallback-model-0")).ToHaveValueAsync(ScriptedModels.Support);

        // Removing the only row returns to the empty-list hint, and a save
        // round trip persists the now-empty list (K1: no lingering fallback).
        await session.Page.GetByTestId("remove-fallback-0").ClickAsync();
        await Expect(session.Page.GetByTestId("fallback-provider-0")).ToHaveCountAsync(0);

        await session.Page.GetByTestId("agent-save").ClickAsync();
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "fallback-agent" })).ToBeVisibleAsync();

        await session.Page.GetByRole(AriaRole.Link, new() { Name = "Edit" }).ClickAsync();

        // The count below is also 0 before the form has loaded; wait for the
        // loaded form first, or the check proves nothing (Phase 184).
        await Expect(session.Page.GetByTestId("agent-name")).ToHaveValueAsync("fallback-agent");
        await Expect(session.Page.GetByTestId("fallback-provider-0")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Context_panel_reflects_selected_strategy_in_request_preview()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/agents/new");

        await session.Page.GetByTestId("agent-name").FillAsync("context-agent");
        await session.Page.GetByTestId("agent-model").FillAsync(ScriptedModels.Default);

        // Conditional fields (trigger, etc.) stay hidden until a strategy is selected.
        await Expect(session.Page.GetByLabel("Trigger: message count")).ToHaveCountAsync(0);

        await session.Page.GetByLabel("Compaction strategy").SelectOptionAsync("SlidingWindow");
        await session.Page.GetByLabel("Trigger: message count").FillAsync("40");
        await session.Page.GetByLabel("Enable todo tracking").CheckAsync();

        var preview = await session.Page.Locator("pre").First.TextContentAsync();

        preview.ShouldNotBeNull();
        preview.ShouldContain("\"strategy\": \"SlidingWindow\"");
        preview.ShouldContain("\"triggerMessages\": 40");
        preview.ShouldContain("\"enableTodo\": true");
    }

    [Fact]
    public async Task Conversation_memory_box_shows_the_search_choice_and_the_request_carries_both()
    {
        // Phase 233 (F-627): one box and one choice. The choice only exists while the box is checked.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/agents/new");

        await session.Page.GetByTestId("agent-name").FillAsync("memory-agent");
        await session.Page.GetByTestId("agent-model").FillAsync(ScriptedModels.Default);

        await Expect(session.Page.GetByTestId("agent-conversation-memory-search")).ToHaveCountAsync(0);

        await session.Page.GetByLabel("Remember the user across sessions", new() { Exact = true }).CheckAsync();
        await session.Page.GetByTestId("agent-conversation-memory-search").SelectOptionAsync("OnDemand");

        var preview = await session.Page.Locator("pre").First.TextContentAsync();

        preview.ShouldNotBeNull();
        preview.ShouldContain("\"enableConversationMemory\": true");
        preview.ShouldContain("\"conversationMemorySearch\": \"OnDemand\"");

        // Clearing the box drops the choice with it: no option is sent that changes nothing.
        await session.Page.GetByLabel("Remember the user across sessions", new() { Exact = true }).UncheckAsync();
        await Expect(session.Page.GetByTestId("agent-conversation-memory-search")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Loop_built_in_the_console_is_saved_read_back_and_runs()
    {
        // Phase 201 (F-302): the console builds the loop, not only carries it. The
        // proof that it runs is the run's own LoopIterationCompleted event.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/agents/new");
        await WaitForDefaultProviderAsync(session.Page);

        await session.Page.GetByTestId("agent-name").FillAsync("loop-agent");
        await session.Page.GetByTestId("agent-model").FillAsync(ScriptedModels.Default);
        await session.Page.GetByLabel("Enable harness features", new() { Exact = true }).CheckAsync();

        await session.Page.GetByTestId("loop-enabled").CheckAsync();
        // Every answer of the scripted echo model starts with "Echo:", so the
        // loop stops after one evaluated iteration. What it echoes is the last
        // user message, which the harness's own context may replace.
        await session.Page.GetByTestId("loop-criterion-marker-0").FillAsync("Echo:");
        await session.Page.GetByTestId("loop-max-iterations").FillAsync("3");
        await session.Page.GetByTestId("loop-on-exhausted").SelectOptionAsync("FailRun");

        await session.Page.GetByTestId("agent-save").ClickAsync();
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "loop-agent", Exact = true })).ToBeVisibleAsync();

        await session.Page.GetByRole(AriaRole.Link, new() { Name = "Edit", Exact = true }).ClickAsync();
        await Expect(session.Page.GetByTestId("agent-name")).ToHaveValueAsync("loop-agent");
        await Expect(session.Page.GetByTestId("loop-criterion-kind-0")).ToHaveValueAsync("completionMarker");
        await Expect(session.Page.GetByTestId("loop-criterion-marker-0")).ToHaveValueAsync("Echo:");
        await Expect(session.Page.GetByTestId("loop-max-iterations")).ToHaveValueAsync("3");
        await Expect(session.Page.GetByTestId("loop-on-exhausted")).ToHaveValueAsync("FailRun");

        await session.Page.GotoAsync($"{host.UiAddress}/playground/loop-agent");
        await session.Page.GetByTestId("playground-input").FillAsync("hello");
        await session.Page.GetByTestId("playground-send").ClickAsync();

        using var api = new HttpClient { BaseAddress = new Uri(host.UiAddress + "/") };

        var run = await WaitUntil.ValueAsync(
            async () => (await api.GetFromJsonAsync<JsonElement>("api/runs?agentName=loop-agent")).EnumerateArray().ToList(),
            static runs => runs.Count == 1 && !string.Equals(runs[0].GetProperty("status").GetString(), "Running", StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(30));

        run[0].GetProperty("status").GetString().ShouldBe("Completed");

        // The events endpoint is a server-sent event stream; for a finished run it
        // replays every recorded event and closes.
        var stream = await api.GetStringAsync($"api/runs/{run[0].GetProperty("id").GetString()}/events");
        stream.Split('\n')
            .Where(static line => line.StartsWith("data: ", StringComparison.Ordinal))
            .Select(static line => EventType(line["data: ".Length..]))
            .Count(static type => string.Equals(type, "LoopIterationCompleted", StringComparison.Ordinal))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Request_fields_and_tool_loop_set_in_the_console_are_saved_and_read_back()
    {
        // Phase 225 (F-598): the model section edits the request fields and the tool-call loop.
        // Both groups are closed while empty, open themselves once a value is stored, and the
        // value the server returns is the value typed.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/agents/new");
        await WaitForDefaultProviderAsync(session.Page);

        await session.Page.GetByTestId("agent-name").FillAsync("sampling-agent");
        await session.Page.GetByTestId("agent-model").FillAsync(ScriptedModels.Default);

        (await session.Page.GetByTestId("sampling-settings").GetAttributeAsync("open")).ShouldBeNull("the group stays closed while it holds nothing");
        (await session.Page.GetByTestId("tool-loop-settings").GetAttributeAsync("open")).ShouldBeNull("the group stays closed while it holds nothing");

        await session.Page.GetByText("Sampling and tool use", new() { Exact = true }).ClickAsync();
        await session.Page.GetByTestId("agent-seed").FillAsync("7");
        await session.Page.GetByTestId("agent-top-k").FillAsync("40");
        await session.Page.GetByTestId("agent-stop-sequences").FillAsync("END\nSTOP");
        await session.Page.GetByTestId("agent-tool-mode").SelectOptionAsync("required");
        await session.Page.GetByTestId("agent-allow-multiple-tool-calls").SelectOptionAsync("false");
        await session.Page.GetByTestId("agent-reasoning-output").SelectOptionAsync("Summary");

        await session.Page.GetByText("Tool-call loop", new() { Exact = true }).ClickAsync();
        await session.Page.GetByTestId("tool-loop-concurrent").CheckAsync();
        await session.Page.GetByTestId("tool-loop-max-errors").FillAsync("1");
        await session.Page.GetByTestId("tool-loop-terminate-unknown").SelectOptionAsync("true");

        await session.Page.GetByTestId("agent-save").ClickAsync();
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "sampling-agent", Exact = true })).ToBeVisibleAsync();

        using var api = new HttpClient { BaseAddress = new Uri(host.UiAddress + "/") };

        var model = (await api.GetFromJsonAsync<JsonElement>("api/agents/sampling-agent")).GetProperty("definition").GetProperty("model");

        model.GetProperty("seed").GetInt64().ShouldBe(7);
        model.GetProperty("topK").GetInt32().ShouldBe(40);
        model.GetProperty("stopSequences").EnumerateArray().Select(static item => item.GetString()).ShouldBe(["END", "STOP"]);
        model.GetProperty("toolMode").GetString().ShouldBe("required");
        model.GetProperty("allowMultipleToolCalls").GetBoolean().ShouldBeFalse();
        model.GetProperty("reasoningOutput").GetString().ShouldBe("Summary");
        model.GetProperty("toolLoop").GetProperty("allowConcurrentToolCalls").GetBoolean().ShouldBeTrue();
        model.GetProperty("toolLoop").GetProperty("maximumConsecutiveErrors").GetInt32().ShouldBe(1);
        model.GetProperty("toolLoop").GetProperty("terminateOnUnknownCalls").GetBoolean().ShouldBeTrue();

        await session.Page.GetByRole(AriaRole.Link, new() { Name = "Edit", Exact = true }).ClickAsync();
        await Expect(session.Page.GetByTestId("agent-name")).ToHaveValueAsync("sampling-agent");

        // A stored value opens its group by itself.
        await Expect(session.Page.GetByTestId("agent-top-k")).ToBeVisibleAsync();
        await Expect(session.Page.GetByTestId("agent-seed")).ToHaveValueAsync("7");
        await Expect(session.Page.GetByTestId("agent-top-k")).ToHaveValueAsync("40");
        await Expect(session.Page.GetByTestId("agent-stop-sequences")).ToHaveValueAsync("END\nSTOP");
        await Expect(session.Page.GetByTestId("agent-tool-mode")).ToHaveValueAsync("required");
        await Expect(session.Page.GetByTestId("agent-allow-multiple-tool-calls")).ToHaveValueAsync("false");
        await Expect(session.Page.GetByTestId("agent-reasoning-output")).ToHaveValueAsync("Summary");
        await Expect(session.Page.GetByTestId("tool-loop-concurrent")).ToBeCheckedAsync();
        await Expect(session.Page.GetByTestId("tool-loop-max-errors")).ToHaveValueAsync("1");
        await Expect(session.Page.GetByTestId("tool-loop-terminate-unknown")).ToHaveValueAsync("true");
    }

    private static string? EventType(string data)
    {
        using var document = JsonDocument.Parse(data);

        return document.RootElement.GetProperty("type").GetString();
    }

    [Fact]
    public async Task Version_diff_compares_two_versions()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await CreateAgentWithTwoVersionsAsync(host, session, "diff-agent", "first instructions", "second instructions");

        await session.Page.GotoAsync($"{host.UiAddress}/agents/diff-agent");
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "diff-agent" })).ToBeVisibleAsync();

        await session.Page.GetByTestId("version-checkbox-1").CheckAsync();
        await session.Page.GetByTestId("version-checkbox-2").CheckAsync();

        // When two versions are selected, both raw definitions are fetched and
        // a line-based diff is rendered; both instruction texts (one as "-",
        // one as "+") must appear.
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Comparing v1 → v2" })).ToBeVisibleAsync();

        // "first instructions"/"second instructions" also appear in the
        // Instructions panel and the raw Definition JSON; diff lines are
        // distinguished by the span.break-all class.
        await Expect(session.Page.Locator("span.break-all", new() { HasText = "first instructions" }).First).ToBeVisibleAsync();
        await Expect(session.Page.Locator("span.break-all", new() { HasText = "second instructions" }).First).ToBeVisibleAsync();
    }
}
