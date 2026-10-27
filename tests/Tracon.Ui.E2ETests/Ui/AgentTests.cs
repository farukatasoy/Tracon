using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    /// <summary>The seed error, whole: <c>Exact</c> matching holds only when the text is the whole message.</summary>
    private const string SeedRangeError =
        "Seed must be a whole number from -9007199254740991 to 9007199254740991. The console cannot send larger numbers exactly. Replace the seed or clear it to save.";

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
    public async Task Workspace_box_shows_its_two_options_and_the_request_carries_all_three()
    {
        // Phase 234 (F-628): one box and two options. The options only exist while the box is checked.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/agents/new");

        await session.Page.GetByTestId("agent-name").FillAsync("workspace-agent");
        await session.Page.GetByTestId("agent-model").FillAsync(ScriptedModels.Default);

        await Expect(session.Page.GetByTestId("agent-workspace-read-only")).ToHaveCountAsync(0);

        await session.Page.GetByTestId("agent-workspace-enabled").CheckAsync();
        await session.Page.GetByTestId("agent-workspace-read-only").CheckAsync();
        await session.Page.GetByTestId("agent-workspace-disable-read-approval").CheckAsync();

        var preview = await session.Page.Locator("pre").First.TextContentAsync();

        preview.ShouldNotBeNull();
        preview.ShouldContain("\"enabled\": true");
        preview.ShouldContain("\"readOnly\": true");
        preview.ShouldContain("\"disableReadApproval\": true");

        // Clearing the box drops the options with it: no setting is sent that changes nothing.
        await session.Page.GetByTestId("agent-workspace-enabled").UncheckAsync();
        await Expect(session.Page.GetByTestId("agent-workspace-read-only")).ToHaveCountAsync(0);
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

    [Fact]
    public async Task A_seed_outside_the_safe_integer_range_is_refused_in_the_form_and_nothing_is_sent()
    {
        // Phase 252 (F-607): the server takes a 64-bit seed, and a browser number is exact up to 2^53 - 1. 9007199254740993
        // typed into the field would go out as 9007199254740992, a seed nobody wrote. The form refuses it instead.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/agents/new");
        await WaitForDefaultProviderAsync(session.Page);

        await session.Page.GetByTestId("agent-name").FillAsync("big-seed-agent");
        await session.Page.GetByTestId("agent-model").FillAsync(ScriptedModels.Default);
        await session.Page.GetByText("Sampling and tool use", new() { Exact = true }).ClickAsync();

        var seed = session.Page.GetByTestId("agent-seed");
        var error = session.Page.GetByText(SeedRangeError, new() { Exact = true });

        await Expect(error).ToHaveCountAsync(0);

        await seed.FillAsync("9007199254740993");

        await Expect(error).ToBeVisibleAsync();
        await Expect(seed).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(session.Page.GetByTestId("agent-save")).ToBeDisabledAsync();
        await Expect(session.Page.GetByTestId("agent-validate")).ToBeDisabledAsync();

        using var api = new HttpClient { BaseAddress = new Uri(host.UiAddress + "/") };

        using (var absent = await api.GetAsync("api/agents/big-seed-agent"))
        {
            absent.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        // The largest seed a browser number holds exactly is accepted, sent as typed, and stored as that number.
        await seed.FillAsync("9007199254740991");

        await Expect(error).ToHaveCountAsync(0);
        await Expect(session.Page.GetByTestId("agent-save")).ToBeEnabledAsync();

        await session.Page.GetByTestId("agent-save").ClickAsync();
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "big-seed-agent", Exact = true })).ToBeVisibleAsync();

        var model = (await api.GetFromJsonAsync<JsonElement>("api/agents/big-seed-agent")).GetProperty("definition").GetProperty("model");

        model.GetProperty("seed").GetInt64().ShouldBe(9007199254740991);
    }

    [Fact]
    public async Task The_seed_range_error_reads_in_Turkish()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host, locale: "tr-TR");

        await session.Page.GotoAsync($"{host.UiAddress}/agents/new");
        await WaitForDefaultProviderAsync(session.Page);

        await session.Page.GetByTestId("agent-name").FillAsync("big-seed-agent-tr");
        await session.Page.GetByTestId("agent-model").FillAsync(ScriptedModels.Default);
        await session.Page.GetByText(TurkishText("agentEditor.sampling"), new() { Exact = true }).ClickAsync();
        await session.Page.GetByTestId("agent-seed").FillAsync("9007199254740993");

        await Expect(session.Page.GetByText(TurkishText("agentEditor.seedError"), new() { Exact = true })).ToBeVisibleAsync();
        await Expect(session.Page.GetByTestId("agent-save")).ToBeDisabledAsync();
    }

    [Fact]
    public async Task A_stored_seed_beyond_the_safe_range_is_flagged_and_the_stored_value_is_not_rewritten()
    {
        // An API client stored 2^53 + 1. The browser reads that response as 9007199254740992, so the form holds a number
        // nobody wrote: it says so, it does not save before the seed is replaced or cleared, and the server's value stays.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        using var api = new HttpClient { BaseAddress = new Uri(host.UiAddress + "/") };

        using (var created = await api.PostAsync(
            "api/agents",
            new StringContent(
                """{"name":"stored-big-seed","instructions":"Answer briefly.","model":{"provider":"scripted","model":"scripted-1","seed":9007199254740993}}""",
                Encoding.UTF8,
                "application/json")))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        await session.Page.GotoAsync($"{host.UiAddress}/agents/stored-big-seed/edit");
        await Expect(session.Page.GetByTestId("agent-name")).ToHaveValueAsync("stored-big-seed");

        var seed = session.Page.GetByTestId("agent-seed");
        var error = session.Page.GetByText(SeedRangeError, new() { Exact = true });

        // The group opens by itself for a stored value; the field shows the rounded number the browser read, with the error.
        await Expect(seed).ToHaveValueAsync("9007199254740992");
        await Expect(error).ToBeVisibleAsync();
        await Expect(session.Page.GetByTestId("agent-save")).ToBeDisabledAsync();

        var stored = (await api.GetFromJsonAsync<JsonElement>("api/agents/stored-big-seed")).GetProperty("definition").GetProperty("model");

        stored.GetProperty("seed").GetInt64().ShouldBe(9007199254740993);

        // Replacing the seed lets the agent be saved, with the new seed.
        await seed.FillAsync("7");

        await Expect(error).ToHaveCountAsync(0);
        await Expect(session.Page.GetByTestId("agent-save")).ToBeEnabledAsync();

        await session.Page.GetByTestId("agent-save").ClickAsync();
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "stored-big-seed", Exact = true })).ToBeVisibleAsync();

        var replaced = (await api.GetFromJsonAsync<JsonElement>("api/agents/stored-big-seed")).GetProperty("definition").GetProperty("model");

        replaced.GetProperty("seed").GetInt64().ShouldBe(7);
    }

    /// <summary>
    /// A Turkish UI text, read from the dictionary the console ships. The source-language gate keeps Turkish out of test
    /// code, and a sentence copied into a test would drift from the dictionary the first time a translation changes.
    /// </summary>
    private static string TurkishText(string key)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName ?? throw new InvalidOperationException("Tracon.slnx not found above the test binary.");
        var dictionary = File.ReadAllText(Path.Combine(root, "src", "Tracon.UI", "frontend", "src", "locales", "tr", "agents.ts"));

        // `"key": 'text',` or `"key": "text",` — a text that holds an apostrophe is written with double quotes.
        var match = Regex.Match(
            dictionary,
            $"\"{Regex.Escape(key)}\":\\s*(?<quote>['\"])(?<text>.*?)\\k<quote>,",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));

        match.Success.ShouldBeTrue($"The Turkish dictionary has no text for '{key}'.");

        return match.Groups["text"].Value;
    }

    private static string? EventType(string data)
    {
        using var document = JsonDocument.Parse(data);

        return document.RootElement.GetProperty("type").GetString();
    }

    [Fact]
    public async Task Callable_agent_pinned_in_the_console_is_saved_read_back_and_sent_to_the_server()
    {
        // Phase 250 (F-409): the editor shows the version a callable agent runs, lets the user pin it, and a
        // save and a reopen keep the choice. The server's own read-back is the second witness: the select
        // could hold a value the request never carried.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await CreateAgentWithTwoVersionsAsync(host, session, "pinned-callee", "first instructions", "second instructions");

        await session.Page.GotoAsync($"{host.UiAddress}/agents/new");
        await WaitForDefaultProviderAsync(session.Page);

        await session.Page.GetByTestId("agent-name").FillAsync("pinning-caller");
        await session.Page.GetByTestId("agent-model").FillAsync(ScriptedModels.Default);

        await session.Page.GetByLabel("pinned-callee", new() { Exact = false }).CheckAsync();

        var version = session.Page.GetByTestId("agent-callable-version-pinned-callee");

        // The list of versions is the callee's own: current, then v2 and v1.
        await Expect(version.Locator("option")).ToHaveCountAsync(3);
        await version.SelectOptionAsync("1");

        await session.Page.GetByTestId("agent-save").ClickAsync();
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "pinning-caller", Exact = true })).ToBeVisibleAsync();

        await session.Page.GetByRole(AriaRole.Link, new() { Name = "Edit", Exact = true }).ClickAsync();
        await Expect(session.Page.GetByTestId("agent-name")).ToHaveValueAsync("pinning-caller");
        await Expect(session.Page.GetByTestId("agent-callable-version-pinned-callee")).ToHaveValueAsync("1");

        using var api = new HttpClient { BaseAddress = new Uri(host.UiAddress + "/") };

        var stored = await api.GetFromJsonAsync<JsonElement>("api/agents/pinning-caller");
        var callable = stored.GetProperty("definition").GetProperty("callableAgents");

        callable.GetArrayLength().ShouldBe(1);
        callable[0].GetProperty("name").GetString().ShouldBe("pinned-callee");
        callable[0].GetProperty("version").GetInt32().ShouldBe(1);
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
